# 步骤、效果观察与功能测试

## 基线与窗口

所有步骤必须有唯一 `id`，步骤开始前自动建立同名基线；场景准备后有 `prepared`，`Checkpoint` 可新增基线。先核对准备，执行原生动作，再用状态或身份变化断言实际效果。

```xml
<Steps>
  <Require id="ready"><ActorState actor="p" state="alive"/></Require>
  <Checkpoint id="before-effect"/>
  <AddHediff id="add" actor="p" def="AlcoholHigh" severity="0.2"/>
  <Wait id="observed" timeoutRealSeconds="5" stableTicks="2">
    <HasHediff actor="p" def="AlcoholHigh" transition="added" since="before-effect"/>
  </Wait>
</Steps>
<Success><StepSucceeded ref="observed"/></Success>
```

这段只能证明 Hediff 添加与观察通路；若要证明技能造成它，改为真实 CastAbility 并配置原生前提，不能用 AddHediff 代替施放。

- Require 当前不满足立即失败。Wait 反复观察，默认实际超时 15 秒；`stableTicks` 按模拟 tick 连续满足计数，同帧/重复帧不增加。
- Wait/叶子 `observation="current|eventually"`；eventually 保留窗口内发生过的匹配，指定 since 则从对应基线观察，适合瞬时效果。
- `Wait observation="always" durationTicks="..."` 要求整个窗口始终满足，任一不满足立即失败；不要与“最终曾出现过”混淆。
- HasHediff/ThingExists 的 `transition="added|removed" since="基线或步骤ID"` 按实例身份判断。改旧 Hediff 严重度不算新增；掉落、转移旧物也不算创建新物。tick 采样与原生事件保留瞬时变化。
- HasHediff/ThingExists/ActorState 可用 `quantifier="any|all|count"`，默认 any；count 配 equals。空的显式目标集合不能通过 all/Not 空真。
- 技能单独记录 `cast.accepted`、`cast.started`、`cast.completed` 和 `effect.observed`。原生资源、次数、冷却、目标与位置拒绝都应保留失败，不补次数、不清冷却。

## 步骤索引

| 步骤 | 主要字段 / 行为 |
| --- | --- |
| Require、Wait | 内含条件；Wait 可设 timeoutRealSeconds/stableTicks/observation/durationTicks |
| Checkpoint | 保存当前对象与健康基线 |
| Screenshot | focus 可为单个 actor，通过正式截图服务；截图不是功能证据 |
| CastAbility | actor/def，target 或 targetX/targetZ 二选一；force 默认 false，原生 job/verb |
| Attack | actor/target；melee=true 为原生近战任务，否则原生攻击；是否命中须另断言 |
| ManTurret | actor 操作者、target 炮塔，等待真实操作关系 |
| BindMech | actor 殖民机械族、target 拥有 Mechlink 的玩家机械师，核对原生监管关系 |
| ApplyDamage | actor 受击者、def DamageDef、amount 正数，可有 armorPenetration/part/instigator，走 TakeDamage |
| Spawn | actor，摆放已准备未生成的对象到场景中心，不创建新对象 |
| CreateThing | def/stuff/x/z/count，在步骤中创建真实新物 |
| Equip、DropEquipment | actor；Equip 有 def/stuff，DropEquipment 原生掉落主武器 |
| AddHediff、RemoveHediff | actor/def；Add 可有 severity/part/durationTicks，Remove 可有 part |
| ExecuteRecipe | actor 工作者、recipe、worktable、output、accelerate=true；Ingredients/Ingredient 的 actor/count 指向精确材料 |
| TransferThing | actor 地上物、target 携带者，整体转移并保留身份 |
| DropInventory | actor 携带者、target 背包物，原生掉落并保留身份 |
| SetRot | actor/state，Fresh / Rotting / Dessicated |
| Resurrect | actor 尸体，原生复活同一个 innerPawn |
| Destroy、Kill | actor，原生销毁 / 死亡 |
| Pause | 暂停模拟，普通回归仍检查实际超时；清理释放暂停绕过 |
| View、SetPlantGrowth | 视图动作见 [views-and-staging.md](views-and-staging.md) |

ExecuteRecipe 校验真实工作台、角色、原生固定筛选、精确材料用量、实际产品及消耗；机械培育/复活使用相应原生路径。output 是本 Case 新对象组引用，可用于后续判据、绑定或截图。这是显式加速功能执行，不能宣称验证长时间工作 AI 或配方账单 UI。

自动炮塔遵守原生索敌，需要合法敌对 Pawn；不能维持的强制墙体目标不是成功样例。装备、动物/机械族/异族等专用 caseType 必须配备真实对应对象和原生动作。

## 条件索引

组合条件为 All/Any/Not，引用必须在规则内存在。

| 条件 | 主要字段 |
| --- | --- |
| HasHediff | actor/def/severityMin/severityMax，可加身份变化与量词 |
| ThingExists | actor 可省略、def 可选；无 actor 检查本场景对象及持有物，可加身份变化与量词 |
| ActorState | actor/state：alive/dead/downed/standing/spawned/destroyed |
| CorpseState | actor/state：valid/spawned/destroyed |
| ActorCount | group/equals |
| Equipped、Wearing、HasAbility | actor/def |
| HasTrait | actor/def/degree |
| SkillLevel | actor/def/min/max |
| StepSucceeded | ref 指向已执行步骤；本身不能承接 Coverage 效果证据 |
| HealthPercent | actor/min/max，Pawn 总体健康或普通 Thing HP 比例 |
| NeedLevel | actor/def/min/max，需要条比例 |
| ResourceLevel | actor/def/min/max，实际有效 GeneDef 资源比例 |
| ModResourceLevel | actor/resource="axolotl.qi"/min/max |
| ComponentState | actor/component：fuel/battery 比例 min/max，power 用 true/false state，powerOutput 用实际 min/max，rot 用腐烂 state |
| EnvironmentState | property=weather 配 def；temperature/sunGlow/skyGlow 配 min/max；unfogged 核对全场解雾 |
| ActorPosition | actor/x/z/radius，radius 默认 0，须仍在测试地图实际生成 |
| ThingHeld | actor 物品、target 携带者 |
| ThingQuantity | actor 或 def，min/max 比较真实堆叠总量 |
| HasController | actor 机械族、target 控制者 |

年龄、外观、基因、地形、物品样式和植物条件见视图参考；它们也可用于准备状态核对。

## 固定 DebugAction

```xml
<DebugAction id="fill" actionId="native.fillNeeds">
  <Select mode="single" actor="p"/>
</DebugAction>
```

| actionId | 真实前提与参数 |
| --- | --- |
| native.fillNeeds | 必须有需要 tracker，填至上限 |
| native.fillHemogen | 有效 Hemogenic 基因，value 0—1，默认 1 |
| native.setPsyfocus | 原生灵能链接，value 0—1，默认 1 |
| native.addHediff / native.removeHediff | def，可有 part；add 可有 severity/durationTicks |
| native.refuel | CompRefuelable，填满 |
| native.chargeBattery | CompPowerBattery，充满 |
| native.healInjuries | 只治疗原生伤势，不复制角色或清空所有 Hediff |
| axolotl.fillQi | 兼容的螈力组件及非零经脉容量，value 0—1，默认 1 |

single 必须命中本场景拥有且已生成的一个有效对象。rectangle 使用 minX/minZ/maxX/maxZ，只选本场景拥有、已生成且活着的 Pawn（可含倒地）。filter 为 allScenePawns/enemies/allies；后两者必须给 relativeTo，并按真实敌对/盟友关系判断。无命中失败，不遍历源地图选目标。

这些是固定注册动作，不接受 XML 提供类名、字段路径或方法，不直接执行互动菜单。若本地新版本另外提供动作绑定扩展，应先核对其独立契约，不从旧设计稿生成代码入口。

准备性调试若修改被观察的对象/Hediff 身份，会记录 `debug.interference`，不能把旧基线后的变化归因于技能。完成准备后建立新的 Checkpoint，再实际施放；仍须具备经脉、功法、基因等真实前提。

## 失败解释

分类有 configuration、prepare、probe、execution、timeout、cleanup、cancelled。配置错误不等于模组 bug；模拟暂停时普通回归仍按帧检查实际期限，主线程完全阻塞由 CLI 心跳/总超时处理。失败保留结果并清理后继续，取消不等于普通失败；不要用重跑丢弃原失败。结合正常游戏路径判定真实问题，避免把人为不可达状态当作必须修复的缺陷。
