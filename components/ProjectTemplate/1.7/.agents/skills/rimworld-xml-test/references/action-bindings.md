# XML 动作绑定

除固定 DebugAction 适配器外，当前实现已支持显式 `ActionBindings`，用于调用已加载模组公开的原生准备接口。它属于调试准备，不自动构成技能效果或 Coverage 证据；不打开互动菜单，也不在 XML 中写代码。

## 定义与调用

Suite 或 Case 下写 ActionBindings；Case 的同 id 定义覆盖 Suite 定义。只将实际被 DebugAction 使用的绑定冻结到 Case，并自动合并其 packageId 到 Requirements；未使用绑定不为其他 Case 引入运行依赖。以下是结构示例，`my.mod`、类型、方法和 Hediff 属性须改成真实公开接口，不能直接作为原版可运行夹具：

```xml
<ActionBindings>
  <ActionBinding id="my.prepare" packageId="my.mod" type="MyMod.PreparationApi" method="Apply">
    <Parameters><Parameter name="level" type="int" min="1" max="20"/></Parameters>
    <Arguments>
      <Argument type="Verse.Pawn" source="selected"/>
      <Argument type="int" parameter="level"/>
    </Arguments>
    <Result equals="Applied"/>
    <ExpectHediff type="MyMod.Hediff_Upgrade" count="1" property="Level" equalsParameter="level"/>
  </ActionBinding>
</ActionBindings>
```

Steps 中调用：

```xml
<DebugAction id="prepare" actionId="my.prepare">
  <Select mode="single" actor="p"/>
  <Arg name="level" value="2"/>
</DebugAction>
<Checkpoint id="after-prepare"/>
```

绑定同样可用于独立群战的同步准备 Steps；擂台 ArenaSuite 不接受 ActionBindings/Steps。绑定调用的 Select 仍只选本场景拥有的有效对象，rectangle 会逐目标调用，不能遍历源地图或世界所有角色。

## 完整字段与约束

| 节点 | 字段及规则 |
| --- | --- |
| ActionBinding | id/packageId/type/method 全必填；id 不可覆盖 native.fillNeeds 等内置动作；type 是限定类名，method 是一个方法标识符，不是调用表达式 |
| Parameters/Parameter | name/type 必填且 name 唯一；支持 int/float/double/bool/string 和已加载公开枚举；min/max 只适用于数字，min ≤ max；每个参数都必须被 Arguments 使用 |
| Arguments/Argument | type 必填；source/parameter/value/def **恰好一个**，按方法参数顺序列出；无参方法也必须写空 Arguments |
| source | 仅 selected，将当前选择对象传给匹配的类型，如 Verse.Pawn |
| parameter | 引用 Parameters 中的名字，两处 type 必须一致，由调用时 Arg 提供值 |
| value | 执行器按声明类型解析标量/枚举，数字必须有限，枚举须为已声明的名称；静态阶段按已知标量类型校验，枚举类型与成员在游戏加载后验证 |
| def | 按 Argument.type 查实际已加载 Def，并校验类型；不是任意对象或字段路径 |
| DebugAction/Arg | name/value 必填；每个声明参数恰好传一次，不接受未知/重复/缺失参数；固定内置动作不接受 Arg |
| Result | 可选，出现时 equals 必填；核对方法真实返回值；void 方法省略 Result |
| ExpectHediff | 可重复；type/def 恰好一个；count 默认 1 且正数，核对选中 Pawn 当前满足类型/Def 条件的 Hediff 精确数量 |
| ExpectHediff.property | 可选的一个公开实例可读属性名，不是字段路径；一旦填写，equals/equalsParameter 必须恰好一个；equalsParameter 必须存在且类型与属性类型一致 |

准备时只解析公开可见、非开放泛型/指针/by-ref 类型；方法须声明在所属 packageId 的类中，满足 **精确 public static 签名**，不接受实例、泛型、ref/out 或自动放宽参数类型。返回值只允许 void、受支持标量或枚举。Def、选中对象类型、标量范围及属性 getter 都在调用前校验；方法异常、返回值不符或后置断言失败保留为失败。

## 静态校验与游戏验证

`Argument.value`、`DebugAction/Arg.value` 按绑定声明的类型校验，不统一按数字检查；bool 的 `true/false`、字符串和具名枚举字面量可经过 XML 加载。已知 int/float/double/bool 参数的格式和声明范围可在 `--validate-only` 时发现；枚举成员、Def、公开方法精确签名及包归属仍在游戏中核对。

`Result.equals`、`ExpectHediff.equals` 同样按实际返回/属性类型解析，可使用匹配的枚举名；不要用数字冒充具名枚举。静态校验通过只表示配置可编译，真实调用与后置断言仍需实际运行。

`ExpectHediff type` 可限定自定义 Hediff 派生类型，其 property 必须是该类型可读取的公开属性；`def` 形式只定位 HediffDef，不承诺可访问自定义派生类属性。该断言检查调用后的总匹配数，不是“本次新增加了几个”。需要证明新增身份时，在准备完成后另建 Checkpoint，再执行真实动作并用 transition/since 观察。

原始日志阶段包括 `debug.binding.prepared`、`debug.binding.result`、`debug.binding.postcondition`；结合实际返回值和后置断言定位失败。准备动作若改变已观察身份仍会触发 debug.interference，不能归因于随后技能。绑定不能代替真实游戏中尚未满足的能力施放前提。
