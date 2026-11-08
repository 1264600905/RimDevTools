# 视图、整套配装与暂存角色

## 视图模式

可用 caseType：HumanoidThreeView、AnimalThreeView、MechanoidThreeView、AlienThreeView、ApparelThreeView、WeaponThreeView、PlantGrowthView、BuildingView、TerrainPatchView、PawnLifeStageThreeView、HairThreeView、HeadTypeThreeView、BodyTypeThreeView、GeneThreeView、XenotypeThreeView、LoadoutView。

使用 `Scene type="studio"`，省略 type 时按类型默认。摄影区优先在主地图安全建立并恢复，没有足够空间时用临时普通主地图；不用口袋地图，也不接受其 generatorDef。

```xml
<View id="outfit" actor="p" directions="South,East,North,West"
      pose="weaponReady" loadout="preserve" cameraSize="11" stableFrames="5"/>
```

- directions 为 1—4 个不重复原生方向；Pawn 默认正/侧/背，静态对象默认一张。不可旋转对象用单张，单格门保留原生连接朝向。
- pose 是 standing/weaponReady/auto。展示站姿不清除真实倒地、年龄或健康；weaponReady 需要真实武器与站立条件。
- `loadout="preserve"` 保留整套装备；`isolate` 必须写 targetDef，只展示指定单件。隔离会改变展示配装，不能再宣称展示的是原完整配装。
- View.cameraSize 默认 11（范围 11—100），stableFrames 默认 5（范围 3—120）；timeoutRealSeconds 默认 15，覆盖整个多方向 View 步骤。同时保留拍摄前实际时间稳定窗口。
- 同一 Pawn 逐方向拍摄并核对身份、外观和配装，默认原图加横排合成图；`composite="false"` 只输出原图。PNG 完成写入并释放编码器后才合成。
- 实际在画面外、迷雾中或地形被裁切不能因有文件而通过。核对截图内容、方向、可见性与装备清单，不能仅看路径。

## 状态与样式

| 内容 | 字段或条件 |
| --- | --- |
| 年龄 | Pawn 的 `Age biologicalYears/chronologicalYears` |
| 外观 | `Appearance` 的 hair/bodyType/headType/hairColor/skinColor |
| 植物 | Thing 的 growth，步骤 `SetPlantGrowth actor/value`，条件 `PlantGrowth actor/min/max` |
| 外观断言 | `PawnAppearance actor/property/value`；property 为 kind/race/gender/hair/bodyType/headType/hairColor/skinColor/lifeStage/biologicalYears/rotation |
| 基因 | `HasGene actor/def/active/xenogene`；`HasXenotype actor/def`，并核对 DLC/模组依赖 |
| 地形 | `TerrainState actor/def` 核对 TerrainArea 全区域 |
| 样式 | 生成项 quality/color/rotation，条件 `ThingStyle actor/stuff/quality/color/rotation` |
| 资源 | Resources 的 GeneResource、Psyfocus、AxolotlQi；验证真实原生值 |

若测试专门外观 Def，要用 PawnAppearance/HasGene 等证明相应设置实际生效，再通过 View 验证渲染。仅生成一个通用角色不构成相应专用类型的完成证据。

## 暂存角色与外部输入

游戏内选中活着 Pawn，开发者动作 `RimDevTools → 暂存测试角色` 捕获到输入项目的 `.tools/ProjectData/temporary-test-pawn.xml`。项目来自最近自动测试启动时 CLI 记录的 `RimDevTools.LastAutoTestProject.json`；无有效记录时需明确项目根目录，不从部署/日志目录猜测。

文件是 schemaVersion 1 的 TemporaryTestPawn 包装及共享 PawnTemplate，带 SHA-256、时间、源身份、录入字段和未录入范围；也支持部分定义的独立 PawnTemplate。修改包装内模板而不更新哈希会拒绝载入，不通过删除校验规避。

可捕获 PawnKind/race/性别/年龄、外观与基因/异种、服装/主武器及材质品质颜色、特性/未禁用技能/热情/Ability、可定位 Hediff 与消失时间、已注册资源。不捕获完整对象图：名字、背景、关系、工作、背包、技能冷却/次数、胡须纹身、未注册私有组件等不等同克隆。相同 Def/部位无法精确区分的多份 Hediff 也会注明。

捕获只读源 Pawn，运行测试创建新 Pawn，提交后冻结数据与哈希。文件由工具原子替换，不复制到证据目录或手工篡改原始捕获。

**已明确交付的外部 file 导入在群战入口：**

```xml
<Templates>
  <PawnTemplate id="captured" file="../../.tools/ProjectData/temporary-test-pawn.xml"/>
  <PawnTemplate id="armed" extends="captured">
    <Equipment mode="patch"><SetPrimary def="Gun_AssaultRifle" quality="Good"/></Equipment>
  </PawnTemplate>
</Templates>
```

相对路径按场景文件目录解析；file 节点只允许 id/file，覆盖写进派生模板。输入只读取一次并冻结内容、哈希和依赖。普通 auto 场景不要假设也有相同 file 导入；可在其 Templates 内编写共享 PawnTemplate 字段，或先核对本地实现。

## 手动窗口的边界

开发者动作 `RimDevTools → 自动测试` 是内置普通测试弹窗：选定义或当前实例，再选战斗/UI/三视图类别，预览适用性并串行提交。它不读取作者场景 XML、旧 CLI 场景缓存、Coverage 或群战；高度自定义脚本仍用 CLI。

一次游戏启动对应 GameLaunchId/ManualSessionId，各提交有 InvocationId；后续提交追加同会话，重启新建。原始机器结果保留成功/失败/不适用，输出由工具分配；切输入项目不改变已建立的输出会话。角色 XML 是数据而非脚本，不把它当作普通窗口可运行外部场景的证据。窗口交互是否人工验收与场景后端能力分别说明。
