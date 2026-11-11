#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;

namespace LogSyncMod.AutoTest
{
    public sealed class CombatMapStyle
    {
        public string Id, Label, Description, Terrain;
        public double Temperature = 21;
        public float Snow;
    }
    public sealed class CombatMapCell
    {
        public int X, Z;
        public string Terrain, Thing, Stuff;
    }
    public sealed class CombatMapPlan
    {
        public CombatMapStyle Style;
        public int Width, Height;
        public int Buildings, RouteCovers;
        public List<CombatMapCell> Cells = new();
    }

    // Shared by the workbench, both XML compilers and the ordinary-map backend.
    // All expensive work happens once during preparation, never during combat ticks.
    public static class CombatMapCatalog
    {
        public static readonly IReadOnlyList<CombatMapStyle> Styles = new[] {
            new CombatMapStyle { Id = "open", Label = "开阔平地", Terrain = "Soil", Description = "无遮拦的普通竞技场，适合比较基础战力。" },
            new CombatMapStyle { Id = "cover", Label = "远程掩体阵地", Terrain = "Gravel", Description = "交错沙袋与射击通道，适合远程对战和掩体换位。" },
            new CombatMapStyle { Id = "mountainRidges", Label = "花岗岩山脊", Terrain = "Gravel", Description = "错落山脊、多条山口与沿路掩体，远近程均可沿侧翼接敌。" },
            new CombatMapStyle { Id = "mountainValley", Label = "砂岩峡谷", Terrain = "Sand", Description = "南北山体夹出宽谷，支脉形成转角与局部隘口。" },
            new CombatMapStyle { Id = "glacier", Label = "冰川雪原", Terrain = "Ice", Temperature = -12, Snow = .22f, Description = "冰面、积雪与岩岛，兼有开阔射界和零散遮挡。" },
            new CombatMapStyle { Id = "urban", Label = "废墟街区巷战", Terrain = "Concrete", Description = "密集建筑、沿路掩体和交叉巷道，可穿屋接敌或沿街区绕侧。" }
        };
        public static CombatMapStyle Get(string id) => Styles.FirstOrDefault(s => s.Id == id)
            ?? throw new ScenarioConfigurationException("未知战场地图：" + id);
        public static string[] Choices => Styles.Select(s => s.Id + "|" + s.Label).ToArray();
        public static void Validate(string id, double density, int clearance)
        {
            Get(id);
            if (!double.IsFinite(density) || density < .5 || density > 1.5) throw new ScenarioConfigurationException("地图疏密必须在 0.5..1.5。");
            if (clearance < 2 || clearance > 12) throw new ScenarioConfigurationException("出生区清理范围必须在 2..12 格。");
        }
        public static void NormalizeScene(XElement scene)
        {
            if (scene.Attribute("battlefield") == null)
            {
                if (scene.Attribute("featureDensity") != null || scene.Attribute("spawnClearance") != null)
                    throw new ScenarioConfigurationException("featureDensity/spawnClearance 需要同时声明 battlefield。");
                return;
            }
            var id = ScenarioLoader.A(scene, "battlefield");
            Validate(id, ScenarioLoader.D(scene, "featureDensity", 1), ScenarioLoader.I(scene, "spawnClearance", 6));
            var environment = scene.Element("Environment");
            if (environment == null) { environment = new XElement("Environment"); scene.AddFirst(environment); }
            if (environment.Attribute("temperature") == null) environment.SetAttributeValue("temperature", Get(id).Temperature);
            if (environment.Attribute("weather") == null) environment.SetAttributeValue("weather", "Clear");
            if (environment.Attribute("roof") == null) environment.SetAttributeValue("roof", "none");
            if (environment.Attribute("naturalSun") == null) environment.SetAttributeValue("naturalSun", ScenarioLoader.A(environment, "weather") == "Clear");
        }
        public static CombatMapPlan Build(string id, int width, int height, int seed, double density = 1)
        {
            Validate(id, density, 6);
            if (width < 30 || height < 30 || width > 250 || height > 250) throw new ScenarioConfigurationException("地图尺寸必须在 30..250。");
            var style = Get(id); var cells = new Dictionary<int, CombatMapCell>(); var random = new Random(seed);
            var buildings = 0; var routeCovers = 0;
            CombatMapCell Cell(int x, int z)
            {
                if (x < 1 || z < 1 || x >= width - 1 || z >= height - 1) return null;
                var key = z * width + x;
                if (!cells.TryGetValue(key, out var value)) cells.Add(key, value = new CombatMapCell { X = x, Z = z });
                return value;
            }
            void Paint(int x, int z, string terrain = null, string thing = null, string stuff = null)
            { var c = Cell(x, z); if (c == null) return; if (terrain != null) c.Terrain = terrain; if (thing != null) { c.Thing = thing; c.Stuff = stuff; } }
            void Ellipse(int cx, int cz, int rx, int rz, string rock, string terrain = null)
            {
                for (var x = cx - rx; x <= cx + rx; x++) for (var z = cz - rz; z <= cz + rz; z++)
                    if ((x - cx) * (x - cx) / (double)(rx * rx) + (z - cz) * (z - cz) / (double)(rz * rz) <= 1)
                        Paint(x, z, terrain, rock);
            }
            void MirrorEllipse(int x, int z, int rx, int rz, string rock)
            { Ellipse(x, z, rx, rz, rock); Ellipse(width - 1 - x, z, rx, rz, rock); }
            var scale = Math.Min(width, height) / 100.0;
            int Size(double n) => Math.Max(1, (int)Math.Round(n * scale * Math.Sqrt(density)));
            if (id == "cover")
            {
                for (var row = 0; row < 6; row++)
                {
                    var z = height * (row + 2) / 9 + random.Next(-2, 3);
                    var x = width * (row % 2 == 0 ? 34 : 43) / 100;
                    for (var n = -Size(2); n <= Size(2); n++)
                    { Paint(x, z + n, "Concrete", "Sandbags", "Cloth"); Paint(width - 1 - x, z + n, "Concrete", "Sandbags", "Cloth"); }
                    for (var n = -Size(3); n <= Size(3); n++)
                    { Paint(x - 1, z + n, "Concrete"); Paint(width - x, z + n, "Concrete"); }
                }
            }
            else if (id == "mountainRidges")
            {
                for (var row = 0; row < 4; row++)
                {
                    var z = height * (row * 2 + 2) / 10;
                    var x = width * (row % 2 == 0 ? 36 : 45) / 100;
                    MirrorEllipse(x + random.Next(-2, 3), z, Size(5), Size(8), row % 2 == 0 ? "Granite" : "Slate");
                }
            }
            else if (id == "mountainValley")
            {
                for (var n = 0; n < 5; n++)
                {
                    var x = width * (n + 1) / 6;
                    Ellipse(x, height * 18 / 100 + random.Next(-2, 3), Size(13), Size(10), "Sandstone");
                    Ellipse(width - 1 - x, height * 82 / 100 + random.Next(-2, 3), Size(13), Size(10), "Sandstone");
                }
                MirrorEllipse(width * 43 / 100, height * 35 / 100, Size(5), Size(9), "Slate");
                MirrorEllipse(width * 33 / 100, height * 65 / 100, Size(5), Size(9), "Slate");
                for (var x = width / 10; x < width * 9 / 10; x++) for (var z = height * 43 / 100; z < height * 57 / 100; z++) Paint(x, z, "Gravel");
            }
            else if (id == "glacier")
            {
                for (var n = 0; n < 5; n++)
                    MirrorEllipse(width * (n % 2 == 0 ? 36 : 44) / 100 + random.Next(-2, 3), height * (n + 2) / 8, Size(3), Size(5), "Granite");
                for (var x = width / 8; x < width * 7 / 8; x++)
                { var z = height / 2 + (int)(Math.Sin(x / 11.0) * Size(6)); for (var n = -1; n <= 1; n++) Paint(x, z + n, "Ice"); }
            }
            else if (id == "urban")
            {
                // Twenty small blocks at ordinary sizes, separated by streets.
                // Keep three-cell openings on all four sides of each building.
                var columns = width >= 80 ? 4 : 2; var rows = height >= 80 ? 5 : 3;
                for (var ix = 0; ix < columns; ix++) for (var iz = 0; iz < rows; iz++)
                {
                    var cx = width * (28 + ix * 44 / Math.Max(1, columns - 1)) / 100;
                    var cz = height * (12 + iz * 76 / Math.Max(1, rows - 1)) / 100;
                    var rx = Math.Min(Size((ix + iz) % 2 == 0 ? 4 : 3), Math.Max(2, width / (columns * 5)));
                    var rz = Math.Min(Size(5), Math.Max(2, height / (rows * 3)));
                    buildings++;
                    for (var x = cx - rx; x <= cx + rx; x++) for (var z = cz - rz; z <= cz + rz; z++)
                    {
                        Paint(x, z, "WoodPlankFloor");
                        var wall = x == cx - rx || x == cx + rx || z == cz - rz || z == cz + rz;
                        var doorway = Math.Abs(z - cz) <= 1 || Math.Abs(x - cx) <= 1;
                        if (wall && !doorway) Paint(x, z, thing: "Wall", stuff: (ix + iz) % 2 == 0 ? "BlocksGranite" : "BlocksSandstone");
                    }
                    if (iz != rows / 2) for (var n = -1; n <= 1; n++) Paint(cx + n, cz + rz + 2, thing: "Sandbags", stuff: "Cloth");
                }
                for (var x = 1; x < width - 1; x++) for (var z = height * 36 / 100; z < height * 40 / 100; z++) Paint(x, z, "PavedTile");
                for (var x = 1; x < width - 1; x++) for (var z = height * 63 / 100; z < height * 67 / 100; z++) Paint(x, z, "PavedTile");
            }
            // Staggered cover along approach lanes, including the central route.
            // These short segments face east/west, with broad gaps between them.
            // Never replace mountain or building walls with a cover prop.
            if (id != "open")
            {
                for (var column = 0; column < 5; column++) for (var lane = 0; lane < 3; lane++)
                {
                    var x = width * (32 + column * 9) / 100;
                    var z = height * (36 + lane * 14 + (column % 2 == 0 ? -3 : 3)) / 100;
                    for (var n = -1; n <= 1; n++)
                    {
                        var c = Cell(x, z + n); if (c == null || c.Thing != null) continue;
                        Paint(x, z + n, thing: "Sandbags", stuff: "Cloth"); routeCovers++;
                    }
                }
            }
            return new CombatMapPlan { Style = style, Width = width, Height = height, Buildings = buildings, RouteCovers = routeCovers,
                Cells = cells.Values.OrderBy(c => c.Z).ThenBy(c => c.X).ToList() };
        }
    }
}
