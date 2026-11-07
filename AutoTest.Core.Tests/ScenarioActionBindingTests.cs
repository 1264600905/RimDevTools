using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

public static class ScenarioActionBindingTests
{
    public enum Outcome { Applied, Protected }
    public sealed class Pawn { public List<Hediff> Hediffs = new(); public int Calls; }
    public class Hediff { public string Def; }
    public sealed class Elite : Hediff { public int Level { get; set; } }
    public sealed class ExampleDef { }
    public static class Api
    {
        public static Outcome Apply(Pawn pawn, int level) { pawn.Calls++; pawn.Hediffs.Add(new Elite { Def = "Elite_" + level, Level = level }); return Outcome.Applied; }
        public static Outcome Refuse(Pawn pawn, int level) { pawn.Calls++; return Outcome.Protected; }
        public static Outcome WrongLevel(Pawn pawn, int level) { pawn.Calls++; pawn.Hediffs.Add(new Elite { Level = level + 1 }); return Outcome.Applied; }
        public static Outcome Throw(Pawn pawn, int level) => throw new InvalidOperationException("native failure");
        public static string Typed(Pawn pawn, ExampleDef def, Outcome result, bool enabled, float scale) => def != null && enabled && scale == 1.5f ? result.ToString() : "failed";
        public static void Widen(object pawn, int level) { }
        public static void ByRef(Pawn pawn, ref int level) { }
    }
    private static string Binding => $"""
        <ActionBinding id="example.elite" packageId="example.mod" type="{typeof(Api).FullName}" method="Apply">
          <Parameters><Parameter name="level" type="int" min="1" max="20"/></Parameters>
          <Arguments><Argument type="{typeof(Pawn).FullName}" source="selected"/><Argument type="int" parameter="level"/></Arguments>
          <Result equals="Applied"/>
          <ExpectHediff type="{typeof(Elite).FullName}" count="1" property="Level" equalsParameter="level"/>
        </ActionBinding>
        """;
    private const string Step = "<DebugAction id='prepare' actionId='example.elite'><Select mode='single' actor='p'/><Arg name='level' value='2'/></DebugAction>";

    internal static void Run(Action<bool, string> check)
    {
        const string body = "<Scene/><Actors><Pawn id='p'><basePawnKind>Colonist</basePawnKind></Pawn></Actors><Steps>" + Step + "<Require id='ready'><ActorState actor='p' state='alive'/></Require></Steps><Success><StepSucceeded ref='ready'/></Success>";
        string Suite(string binding = null, string content = body) => "<TestSuite schemaVersion='1' id='bindings'><ActionBindings>" + (binding ?? Binding) + "</ActionBindings><Case id='a'><testDefName>BoundAction</testDefName>" + content + "</Case></TestSuite>";
        ScenarioPlan Compile(string xml) => ScenarioLoader.Compile(new[] { new ScenarioInput { Path = "bindings.xml", Sha256 = "hash", Xml = xml } });
        void Reject(Action action, string message, string contains = null)
        {
            try { action(); check(false, message); }
            catch (ScenarioConfigurationException ex) { check(contains == null || ex.Message.Contains(contains), message); }
        }
        var plan = Compile(Suite()); var frozen = XElement.Parse(plan.Cases.Single().Xml);
        check(frozen.Element("ActionBindings").Elements().Count() == 1 && frozen.Element("Requirements").Element("Mod").Attribute("packageId").Value == "example.mod", "suite action bindings and their mod dependency freeze into the runnable case");
        var unbound = Compile(Suite(content: body.Replace(Step, "")));
        check(XElement.Parse(unbound.Cases.Single().Xml).Element("ActionBindings") == null && XElement.Parse(unbound.Cases.Single().Xml).Element("Requirements") == null, "unused action bindings do not require their mod in unrelated cases");
        Reject(() => Compile(Suite().Replace("value='2'", "value='0'")), "binding bounds reject an ordinary level before launching", "bindings.xml");
        Reject(() => Compile(Suite().Replace("value='2'", "value='1.5'")), "binding integer parameters reject fractions before launching");
        Reject(() => Compile(Suite().Replace("value='2'", "value='NaN'")), "binding numeric parameters reject nonfinite values");
        Reject(() => Compile(Suite().Replace("<Arg name='level' value='2'/>", "")), "missing binding parameters fail compilation");
        Reject(() => Compile(Suite().Replace("</DebugAction>", "<Arg name='level' value='3'/></DebugAction>")), "duplicate action arguments fail compilation");
        Reject(() => Compile(Suite().Replace("name='level'", "name='typo'")), "unknown action arguments fail compilation");
        Reject(() => Compile(Suite(Binding + Binding)), "duplicate XML action binding ids fail compilation");
        Reject(() => Compile(Suite(Binding.Replace("example.elite", "native.fillNeeds"))), "XML bindings cannot shadow built-in adapters");
        Reject(() => Compile(Suite(Binding.Replace("property=\"Level\"", "property=\"Private.Level\""))), "property checks require a single property rather than a field path");
        Reject(() => Compile(Suite(Binding.Replace("parameter=\"level\"", "parameter=\"missing\""))), "binding method arguments must reference declared parameters");
        Reject(() => Compile(Suite(Binding.Replace("source=\"selected\"", "source=\"allWorldPawns\""))), "binding target source stays explicit");
        Reject(() => Compile(Suite(Binding.Replace("method=\"Apply\"", "method=\"Apply\" Class=\"Anything\""))), "binding XML still rejects unsupported fields");

        ScenarioBoundAction Executor(string xml = null, Func<Type, bool> owner = null) => new(XElement.Parse(xml ?? Binding), name => typeof(Api).Assembly.GetType(name), owner ?? (_ => true),
            (type, name) => type == typeof(ExampleDef) && name == "TestDef" ? new ExampleDef() : throw new ScenarioConfigurationException("missing Def"),
            typeof(Hediff), p => ((Pawn)p).Hediffs, h => ((Hediff)h).Def);
        var pawn = new Pawn(); var traces = new List<string>();
        Executor().Execute(XElement.Parse(Step), pawn, (stage, text) => traces.Add(stage + ":" + text));
        check(pawn.Calls == 1 && pawn.Hediffs.OfType<Elite>().Single().Level == 2 && traces.Any(t => t.Contains("actual=2; expected=2")), "XML binding invokes the native pipeline once and checks the actual resulting level");
        pawn = new Pawn();
        Reject(() => Executor().Execute(XElement.Parse(Step.Replace("value='2'", "value='0'")), pawn, null), "invalid runtime arguments fail before native mutation");
        check(pawn.Calls == 0, "failed runtime validation does not invoke the native method");
        Reject(() => Executor(owner: _ => false), "declared method type must belong to the loaded package");
        Reject(() => Executor(Binding.Replace("method=\"Apply\"", "method=\"Widen\"")), "method signature does not silently widen the selected Pawn to object");
        Reject(() => Executor(Binding.Replace("method=\"Apply\"", "method=\"ByRef\"")), "ref parameters do not match a value parameter binding");
        Reject(() => Executor().Execute(XElement.Parse(Step), new object(), null), "selected target must have the declared native type");
        Reject(() => Executor(Binding.Replace("method=\"Apply\"", "method=\"Refuse\"" )).Execute(XElement.Parse(Step), new Pawn(), null), "native Protected outcome cannot pass as a successful elite preparation", "unexpected result");
        Reject(() => Executor(Binding.Replace("method=\"Apply\"", "method=\"WrongLevel\"" )).Execute(XElement.Parse(Step), new Pawn(), null), "matching return value cannot hide an incorrect resulting elite level", "postcondition failed");
        Reject(() => Executor(Binding.Replace("method=\"Apply\"", "method=\"Throw\"" )).Execute(XElement.Parse(Step), new Pawn(), null), "native exceptions retain their diagnostic message", "native failure");
        Reject(() => Executor(Binding.Replace("property=\"Level\"", "property=\"Missing\"")), "missing native property is rejected before calling the action");
        var typed = $"""
            <ActionBinding id="example.typed" packageId="example.mod" type="{typeof(Api).FullName}" method="Typed">
              <Arguments><Argument type="{typeof(Pawn).FullName}" source="selected"/><Argument type="{typeof(ExampleDef).FullName}" def="TestDef"/>
                <Argument type="{typeof(Outcome).FullName}" value="Applied"/><Argument type="bool" value="true"/><Argument type="float" value="1.5"/></Arguments>
              <Result equals="Applied"/>
            </ActionBinding>
            """;
        Executor(typed).Execute(XElement.Parse("<DebugAction actionId='example.typed'><Select mode='single' actor='p'/></DebugAction>"), new Pawn(), null);
        check(true, "generic bindings support Def arguments, named enums, booleans and finite float constants");
        Reject(() => Executor(typed.Replace("value=\"Applied\"", "value=\"999\"")), "undefined numeric enum values are rejected before invocation");
        Reject(() => Executor(typed.Replace("def=\"TestDef\"", "def=\"MissingDef\"")), "missing Def arguments fail before native mutation");

        var fixture = ScenarioLoader.Load("AutoTest.Core.Tests/Fixtures/GroupCombatBulbfreaksVsHussars.autotest.xml", true);
        var eliteCases = fixture.Cases.Where(c => c.TestDefName.StartsWith("Elite")).Select(c => XElement.Parse(c.Xml)).ToList();
        check(eliteCases.Count == 2 && eliteCases.Select(c => c.Element("Steps").Element("DebugAction").Element("Arg").Attribute("value").Value).SequenceEqual(new[] { "1", "2" }), "inherited elite battles patch XML binding arguments to levels one and two");
        check(fixture.Cases.Where(c => !c.TestDefName.StartsWith("Elite")).All(c => XElement.Parse(c.Xml).Element("ActionBindings") == null), "existing animal and mech battles retain no elite action dependency");
    }
}
