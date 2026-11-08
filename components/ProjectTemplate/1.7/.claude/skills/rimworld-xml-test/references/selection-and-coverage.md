# 选择、参数矩阵与普通回归覆盖

## 命令选择

```powershell
# 仅外部规则；把批量上限设为预期物理实例数量并核对结果
.\.tools\RimDevTools.exe test run --mode auto --scenario-only --scenario-path "Tests/Scenarios" --batch-size 20 --timeout-seconds 600

# 与普通 UI、远程武器测试混合；DefName 每个值重复给一次
.\.tools\RimDevTools.exe test run --mode auto --scenario-path "Tests/Scenarios/ranged-matrix.autotest.xml" --scope ui --scope rangedweapons --defname Gun_Autopistol --defname Gun_Revolver --batch-size 10 --timeout-seconds 600
```

- `--scenario-path` 接受一个文件或目录；目录只读当前层 `*.autotest.xml`，按文件名排序。不要让同一枚举范围包含 GroupCombat。
- `--scenario-only` 仅运行外部回归，不能用于 groupcombat/arena。
- `--test-name` 可重复；普通外部回归接受稳定 testDefName 或唯一显示名，未知/重名报错。稳定名选中其矩阵实例作为候选，并不保证所有候选都逃过批量限制。完整 caseId 的精确选择在群战入口有明确契约，不假设普通入口也支持。
- `--defname` 用于普通目标，不是测试规则名；多值重复传参，不写逗号列表。
- `--scope`、`--defname`、`--batch-size` 可与外部回归混用。早期第一批文档中的禁止混用是历史限制。
- 没有场景参数时继续使用普通测试选择策略。勿根据 XML 文件数估算运行数；一个 Case 可展开为多个物理实例。

## 显式 Matrix

Matrix 在 Case 内，用 Row，不是任意笛卡尔积生成器。行 ID 按大小写不敏感唯一，最多 256 行。

```xml
<Matrix>
  <Row id="autopistol"><Param name="weapon" value="Gun_Autopistol"/></Row>
  <Row id="revolver"><Param name="weapon" value="Gun_Revolver"/></Row>
</Matrix>
```

`${weapon}` 可替换属性和叶子文本，值写入 XML 节点，不能注入标签/代码或递归参数。未知、重复、未使用的参数及替换后的非法字段失败；testDefName 不允许参数化。示例资产使用同一武器参数同时配置主武器和准备断言，保证参数真正参与执行。

## Coverage

在 Case 内与 Scene/Actors/Steps 并列填写；缺省不写即可执行独立补充场景。`mode="supplement"` 保留普通用例，`replace` 以已验证外部功能证据承接精确普通键。

```xml
<Coverage mode="replace">
  <Target packageId="ludeon.rimworld" defType="ThingDef"
          defName="${weapon}" caseType="RangedWeaponPocketMap">
    <Evidence step="damage"/>
  </Target>
</Coverage>
```

`damage` 必须是验证真实状态/效果的 Require 或 Wait。截图、Checkpoint 或仅 StepSucceeded 的步骤不能作为覆盖证据。目标须实际参与角色/步骤；单纯拥有能力或声明名字不足以承接技能效果。

精确键为 `packageId + defType + defName + caseType + variant`。packageId 省略沿用 Suite；空 variant 只匹配没有变体的普通用例。服装/物品变体如 `Colonist@Male`，格式是 `PawnKindDef@BodyTypeDef`，以本轮普通 manifest 的具体值为准，不用通配符。缺失 Def、错误来源/类型/变体以及两个 replace 争用相同完整键都失败。

一个联合 Case 可声明多个目标，各自关联对应证据；一个物理联合 Case 只占一个批量额度。只有整个场景、目标实际使用记录、清理与语义核对通过，声明目标才进入 `verifiedCoverageKeys`。失败、未执行、跳过或不支持不能产生绿色覆盖。

普通 UI 键不能被功能场景替代。`SkillPocketMap` 不承接 `AbilityGizmoUi`；`RecipeExecution` 不承接 `RecipeBillUi` 或信息卡。用户若要求同时验证 UI，保留普通范围与足够批量额度，并核对这些 UI 项实际被选中。

## 选择与结果核对

替代优先：保留范围内全部替代，移除被承接的普通用例，再按物理 Case 补足其他范围内候选，有余量才选范围外 XML。替代超过批量上限仍保留，记录 `ReplacementLimitExceeded`。执行顺序为替代/范围内场景、剩余普通用例、范围外场景。

读取机器结果的完整 `scenarioSelection`、选择/批量原因、实际替代关系、场景结果、已验证键及物理执行数。`ExternalScenario` 表示仅外部计划，`MixedScenario` 表示混合计划；Completeness 只说明本次选中计划完成度，不表示全模组回归覆盖。
