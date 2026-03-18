# CombatPawn 受击测试判定问题分析

## 你的目标语义

你这次补充的目标非常明确：

1. 受击测试是“一群敌人围殴一个穿着测试装备的小人”。
2. 这里的“测试目标”是我方那个穿着测试装备的小人。
3. 提前结束条件应该看这个测试目标是否倒下或死亡。
4. 不是看敌人是否倒下。
5. 如果测试目标没有倒下，才走 60 秒存活结束。

换句话说，受击测试的主语必须是“被围殴的我方测试小人”，不是敌人。

## 当前源码现状

### 1. `CombatPawn` 模块的结束判定主逻辑

文件：

- [AutoTestRunner.CombatPawnModule.cs](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs)

关键实现：

- 在 `SpawnTargetPawn()` 中生成我方测试目标。
- 在 `SpawnEnemies(5)` 中生成 5 个敌人。
- 在 `MaintainEnemyAttackOrders()` 中持续给敌人下达 `AttackMelee` 命令，目标是测试目标。
- 在 `UpdateFighting()` 中决定什么时候结束测试。

源码上，当前 `UpdateFighting()` 的结束逻辑是：

- 如果 `trackedTarget == null || trackedTarget.Destroyed || trackedTarget.Dead || trackedTarget.Downed`
  - 直接通过并结束
  - `Downed` 记为 `target_downed`
  - `Dead` 记为 `target_dead`
- 否则继续战斗
- 如果战斗时间耗尽，则记为 `survived_60s`

对应源码位置：

- [AutoTestRunner.CombatPawnModule.cs#L261](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs#L261)

### 2. 当前 `CombatPawn` 模块并不是“按敌人倒下结束”

就当前源码来说，`CombatPawn` 模块的主结束判定并没有写成“敌人倒下就结束”。

当前实现里：

- `EnemyPawns` 只用于找主敌人、维持攻击命令、做截图取景。
- 提前结束判断用的是 `trackedTarget`。

对应源码位置：

- [AutoTestRunner.CombatPawnModule.cs#L103](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs#L103)
- [AutoTestRunner.CombatPawnModule.cs#L261](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs#L261)
- [AutoTestRunner.CombatPawnModule.cs#L361](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs#L361)

也就是说，严格按现有代码读，`CombatPawn` 模块的结束主体已经是“测试目标”，不是敌人。

## 真正的问题在哪里

### 问题 1：虽然现在已经引入了“目标标记 hediff”，但这个 hediff 不是纯标记

当前为了让 `CombatPawn` 更稳定地识别测试目标，我把测试目标挂上了：

- `LogSync_TurretTestTarget`

对应源码位置：

- [AutoTestRunner.CombatPawnModule.cs#L155](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs#L155)

但这个 hediff 在定义上不是一个“纯标记 hediff”，而是一个带强功能效果的 hediff：

- `IncomingDamageFactor = -0.9`
- 也就是只承受约 10% 伤害
- 立即晕眩一次
- 每移动一格再晕眩一次

对应定义位置：

- [TestDefs.xml#L88](D:/RimworldProject/环世界日志同步mod/1.6/Defs/Internal/TestDefs.xml#L88)
- [LogSyncTurretTestTargetHediffComp.cs#L67](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/LogSyncTurretTestTargetHediffComp.cs#L67)

这意味着：

1. 这个 hediff 虽然能当“标记”，但同时也把测试目标变成了高减伤单位。
2. 受击测试目标因此非常不容易倒下。
3. 结果就是大部分 case 都会落到 `survived_60s`，而不是 `target_downed`。

这正是你看到“怎么可能是存活”的根本原因。

不是因为当前 `CombatPawn` 代码还在看敌人倒下，而是因为当前“标记方案”把测试目标本身变得太耐打了。

### 问题 2：当前结果体系里，`survived_60s` 仍然是合法通过分支

当前 `CombatPawn` 模块仍然保留：

- 倒下/死亡则提前通过
- 否则存活 60 秒也通过

对应源码位置：

- [AutoTestRunner.CombatPawnModule.cs#L289](D:/RimworldProject/环世界日志同步mod/source/AutoTestProject/AutoTestRunner.CombatPawnModule.cs#L289)

这和你现在强调的观察重点并不冲突，但会造成认知偏差：

1. 你在现场看到的预期是“测试目标倒下就该立刻结束”。
2. 代码里确实保留了这个分支。
3. 但因为测试目标现在被 `LogSync_TurretTestTarget` 加了高减伤，所以更常见的是 60 秒存活通过。
4. 最终观感就像是“系统根本没在关心测试目标倒下”。

实际上，当前更准确的说法应该是：

- 系统已经会关心测试目标倒下
- 但测试目标被错误地强化了，导致这个分支很难被命中

### 问题 3：当前“目标标记”和“目标效果”被混在了一个 hediff 里

这是当前设计里最大的问题。

你想要的是：

- 一个稳定的“这是测试目标”的标记

但当前实际使用的是：

- 一个同时承担“目标标记 + 炮塔/武器靶子行为修饰”的 hediff

这两个职责不应该混在一起，因为它们的目的完全不同：

1. 标记职责：只负责告诉系统“谁是受击测试的主目标”
2. 行为职责：改变伤害、移动、控制效果

当前 `LogSync_TurretTestTarget` 明显属于炮塔/武器测试靶子用的功能性 hediff，不适合作为 `CombatPawn` 受击测试的纯目标标记。

## 当前实现与目标需求的差异

### 你要的

- 受击测试里，我方穿测试装备的小人是主目标
- 这个主目标倒下/死亡时立即结束
- 60 秒只应该作为兜底结束
- 目标标记应该稳定，但不该影响这个目标本身的受击语义

### 当前实现

- 识别主体：已经开始向“测试目标自身”靠拢
- 提前结束：已经支持 `target_downed` / `target_dead`
- 兜底结束：仍保留 `survived_60s`
- 目标标记：错误复用了 `LogSync_TurretTestTarget`
- 副作用：测试目标被附带了高减伤和眩晕效果

所以当前真正的偏差不是“代码仍按敌人倒下判定”，而是：

- 目标识别已经改向正确方向
- 但标记 hediff 的选择是错的，导致测试目标本身被强化
- 于是倒地下线分支几乎不会发生

## 为什么你会感觉“现在全部判定都是敌人倒下”

从现场体验上，这个判断是可以理解的，原因有三个：

1. 你期待的主事件是“测试目标被围殴后倒下”
2. 当前测试目标因为 `LogSync_TurretTestTarget` 被大幅减伤，所以经常扛住
3. 最终看到的结果总是 `survived_60s`

这就会形成一个非常强的错觉：

- “系统没有在看测试目标倒下”

但从源码上看，更准确的结论是：

- 系统在看测试目标倒下
- 只是这个测试目标被不该有的 hediff 效果保护住了

## 后续修正方向

本次文档只做分析，不修改代码。后续如果要按你的目标修正，方向应该是：

1. `CombatPawn` 受击测试必须使用“纯标记 hediff”
   - 只做目标识别
   - 不做减伤
   - 不做 stun
   - 不改移动速度

2. `LogSync_TurretTestTarget` 不能继续拿来做 `CombatPawn` 主目标标记
   - 它适合炮塔/武器靶子测试
   - 不适合受击测试主目标

3. `CombatPawn` 的核心判定主体要继续保持为“带目标标记 hediff 的 pawn”
   - 这点方向是对的
   - 但 marker 必须去功能化

4. `survived_60s` 应继续作为兜底通过
   - 因为你之前对战斗测试的整体定义就是“战死或者坚持 60 秒都算成功”
   - 但在受击测试里，现场观察重点必须优先看“标记目标是否倒下并提前关闭”

## 结论

当前 `CombatPawn` 受击测试的问题，根因不是“源码仍在按敌人倒下结束”。

真正的问题是：

1. 受击测试主目标现在已经改成了“带标记 hediff 的测试 pawn”
2. 但这个标记 hediff 错用了 `LogSync_TurretTestTarget`
3. 该 hediff 自带 90% 减伤和眩晕效果
4. 这使得被围殴的测试目标很难倒下
5. 所以结果大多落到 `survived_60s`
6. 你的现场观感就会变成“系统没有在按测试目标倒下结束”

如果后续要真正符合你的目标，关键不是继续改“看谁倒下”，而是把“受击测试目标标记”从功能性 hediff 里拆出来，做成纯标记。
