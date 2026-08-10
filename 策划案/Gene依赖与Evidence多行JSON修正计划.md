# Gene 依赖与 Evidence 多行 JSON 修正计划

## 1. 文档目的

本文用于交接下一轮开发，解决当前自动测试中的两个问题：

1. GeneThreeView 的“严格单 Gene”模型没有考虑 `GeneDef.prerequisite`；
2. Evidence.json 当前为单行压缩 JSON，不利于人工检查和 AI 分段读取。

本文只描述实施方案，不包含本轮代码修改。

当前实现基线为提交：

    80dd9c1 feat: 完善三视图与自动测试产物

当前发布版本：

    About: 1.1.4
    LogSyncMod.dll: 1.1.4.0
    LogSyncMod.AutoTest.dll: 1.1.4.0

## 2. 已确认的问题

### 2.1 Gene prerequisite 没有被当前测试补齐

RimWorld 1.6 的 `GeneDef` 包含：

    public GeneDef prerequisite;

例如：

    Deathrest -> Hemogenic
    HemogenDrain -> Hemogenic
    PiercingSpine -> Hemogenic

`Pawn_GeneTracker.AddGene` 不会自动添加 `GeneDef.prerequisite`。

当前 GeneThreeView 流程是：

1. 清除全部 Endogene；
2. 清除全部 Xenogene；
3. 重置为 Baseliner；
4. 只添加目标 Gene；
5. 断言 Pawn 的 Gene 总数等于 1。

因此测试 Deathrest 时，Pawn 实际只有 Deathrest，没有 Hemogenic。这不是目标 Gene 的合法完整环境。

### 2.2 当前 Evidence 是单行 JSON

当前序列化入口：

    source/AutoTestProject/Core/AutoTestEvidenceBuilder.cs
    AutoTestEvidenceSerializer.Write

当前实现直接调用：

    new DataContractJsonSerializer(typeof(AutoTestEvidenceManifest))
        .WriteObject(stream, manifest);

`DataContractJsonSerializer.WriteObject(Stream, ...)` 默认输出单行 JSON。

Evidence 内容较大，包含：

- environment；
- operations；
- assertions；
- captures；
- logs；
- cases；
- events；
- readiness。

单行输出会导致：

- AI 很难按对象或数组分段读取；
- 文本查看器横向内容过长；
- Git diff 和人工排查几乎不可用；
- 搜索命中时往往返回整份文件。

## 3. 总体设计决策

### 3.1 “单 Gene 测试”改为“单目标 Gene 测试”

“单 Gene”不再表示 Pawn 总 Gene 数必须恒等于 1。

新的定义是：

- 目标 Gene 必须恰好存在一份；
- 可以存在目标 Gene 的 prerequisite 依赖闭包；
- 不允许存在目标及其依赖之外的 Gene；
- 三个正式 Pawn 的 Gene 集合必须完全一致。

无依赖 Gene 的行为保持不变：

    ExpectedTotalGeneCount = 1

有依赖 Gene 的示例：

    TargetGene = Deathrest
    DependencyGenes = [Hemogenic]
    ExpectedGeneSet = [Hemogenic, Deathrest]
    ExpectedTotalGeneCount = 2

### 3.2 外观差异必须排除依赖 Gene 自身的影响

不能使用：

    零基因 -> 依赖 Gene + 目标 Gene

进行比较，因为差异中会混入 prerequisite 的外观效果。

正确比较方式：

    Before = 只包含 prerequisite 依赖闭包
    After  = prerequisite 依赖闭包 + 目标 Gene

因此目标 Gene 的外观差异只来自最后加入的目标 Gene。

以 Deathrest 为例：

    Before = Hemogenic
    After  = Hemogenic + Deathrest

Deathrest 仍很可能得到 `skipped/no_visual_effect`，但该结论是在合法依赖环境下产生。

### 3.3 Evidence 只改变排版，不改变 Schema

Evidence 多行化要求：

- 保持 `schemaVersion = 1`；
- 不改字段名称；
- 不改变字段类型；
- 不改变数组顺序；
- 不改变 `/Date(...)\/` 日期表示；
- `AutoTestEvidenceSerializer.Read` 保持兼容；
- RimDevTools 的 Evidence 校验逻辑不需要感知排版变化。

本次只要求 Evidence.json 多行展开。

除非另有需求，不同时修改：

- Result.json；
- Manifest.json；
- Settings JSON；
- CLI 标准输出 JSON。

## 4. Gene 依赖解析方案

### 4.1 依赖来源

第一版只处理 RimWorld 原生明确声明的：

    GeneDef.prerequisite

不通过名称、分类或描述猜测隐式依赖。

不自动把“依赖当前 Gene 的其他 Gene”加入测试。

例如测试 Hemogenic 时：

- 不加入 Deathrest；
- 不加入 HemogenDrain；
- 不加入 PiercingSpine。

因为这些是 Hemogenic 的反向依赖，不是 Hemogenic 自身的 prerequisite。

### 4.2 递归依赖闭包

新增统一解析方法，例如：

    TryResolveGenePrerequisites(
        GeneDef target,
        out List<GeneDef> orderedDependencies,
        out string errorCode,
        out string errorMessage)

规则：

1. 从 `target.prerequisite` 开始向上递归；
2. 最上层依赖先加入，目标的直接依赖最后加入；
3. 按依赖拓扑顺序添加到 Pawn；
4. 使用 `defName` 或对象引用去重；
5. 使用 visiting/visited 集合检测循环；
6. 不把目标 Gene 自身加入 dependency 列表。

示例：

    GeneC.prerequisite = GeneB
    GeneB.prerequisite = GeneA

输出：

    [GeneA, GeneB]

最终集合：

    [GeneA, GeneB, GeneC]

### 4.3 异常结果

建议错误码：

- `gene_dependency_cycle`：检测到循环依赖；
- `gene_dependency_missing`：依赖引用无法解析；
- `gene_dependency_add_failed`：依赖未能加入 GeneTracker；
- `gene_dependency_inactive`：依赖加入后不是 Active；
- `gene_target_inactive`：目标加入后不是 Active；
- `gene_expected_set_mismatch`：最终集合不等于目标加依赖闭包。

普通 `prerequisite == null` 不是异常，依赖集合为空。

## 5. Gene 探测流程修正

### 5.1 当前需要拆分的逻辑

当前 `TryResolveAppearancePresenter` 中：

1. 清空 Gene；
2. 记录零基因签名；
3. `ApplyAppearanceTarget` 再次清空 Gene；
4. 只添加目标 Gene；
5. 比较签名。

该结构不能正确表达“依赖基线”。

建议将 Gene 路径拆成两个明确步骤，不再完全复用普通 Hair/Head/Body 的单步应用逻辑。

### 5.2 准备依赖基线

新增方法，例如：

    PrepareGeneDependencyBaseline(Pawn pawn, AutoTestThreeViewTargetSpec spec, out string error)

流程：

1. `ClearAllGenes(pawn)`；
2. 将 Xenotype 重置为 Baseliner；
3. 按拓扑顺序添加全部 prerequisite；
4. 默认全部作为 Xenogene 添加；
5. 同步刷新 GeneTracker、Renderer 和 HAR Graphic；
6. 同步重建 RenderTree；
7. 断言实际集合精确等于 dependency 集合；
8. 断言每个 prerequisite `Active == true`；
9. 记录依赖基线外观签名。

默认使用 Xenogene 的原因：

- 清理稳定；
- 位置容易记录；
- 不依赖自然遗传语义；
- 与当前目标 Gene 添加位置一致。

### 5.3 添加目标 Gene

新增方法，例如：

    ApplyTargetGeneToDependencyBaseline(Pawn pawn, AutoTestThreeViewTargetSpec spec, out string error)

流程：

1. 确认当前 Pawn 只有 prerequisite 依赖集合；
2. 添加目标 Gene；
3. 同步刷新 Graphic 和 RenderTree；
4. 确认目标 Gene 存在且 Active；
5. 确认最终集合精确等于依赖闭包加目标；
6. 确认没有随机 Endogene/Xenogene 回流；
7. 记录应用后外观签名；
8. 比较 Before/After。

### 5.4 无视觉效果处理

如果合法依赖环境下 Before/After 相同：

    skipped/no_visual_effect

这不影响 GeneInfoCard。

Deathrest 的预期结果通常是：

    GeneInfoCard = passed
    GeneThreeView = skipped/no_visual_effect
    DependencyChain = Hemogenic

### 5.5 非外观功能测试不在本次范围

GeneThreeView 只负责视觉差异，不验证：

- Deathrest Need 是否随时间变化；
- Hemogen 资源条是否消耗；
- Ability 是否能释放；
- 建筑绑定；
- 长时间 Tick 行为。

如果后续要验证这些功能，应新增 GeneFunctional 或专用能力测试，不要让 GeneThreeView 承担行为测试。

## 6. 正式三视图 Gene 集合规则

### 6.1 Spec 新增字段

在 `AutoTestThreeViewTargetSpec` 中建议增加：

    List<GeneDef> TargetGeneDependencies
    List<GeneDef> ExpectedGeneSet
    string GeneDependencyChain

其中：

    ExpectedGeneSet = TargetGeneDependencies + TargetGeneDef

### 6.2 Context 新增或替换字段

当前字段：

    TargetGeneCount
    TargetEndogeneCount
    TargetXenogeneCount
    TargetGeneOnly
    TriptychGeneSetsMatch

建议调整为：

    TargetGeneCount
    DependencyGeneCount
    ExpectedTotalGeneCount
    ActualTotalGeneCount
    TargetEndogeneCount
    TargetXenogeneCount
    DependencyGenesPresent
    OnlyTargetAndDependencies
    TriptychGeneSetsMatch
    GeneDependenciesActive
    TargetGeneActive

`TargetGeneOnly` 语义已经不准确，建议替换为：

    OnlyTargetAndDependencies

如果为了兼容报告字段保留旧字段，也必须停止用它表达“总数等于 1”。

### 6.3 三只 Pawn 的断言

每只正式 Pawn 都必须满足：

    TargetGeneCount == 1
    ActualGeneSet == ExpectedGeneSet
    ActualTotalGeneCount == ExpectedTotalGeneCount
    NoUnexpectedGenes == true
    AllExpectedGenesActive == true

三个 Pawn 之间：

    GeneSetsEqual == true

### 6.4 外观模板复制

`AutoTestThreeViewAppearanceTemplate` 和 `SyncGenes` 必须复制完整预期集合，而不是只复制目标 Gene。

复制完成后需要再次验证：

- prerequisite 没有丢失；
- 目标 Gene 没有重复；
- 原随机 Gene 没有恢复；
- 三个 Pawn 的 Gene 位置和集合一致。

## 7. Gene Notes 与 Evidence 调整

### 7.1 Result Notes

建议记录：

    TargetGeneDef=Deathrest
    TargetGeneLocation=Xenogene
    TargetGeneCount=1
    GeneDependencyChain=Hemogenic
    DependencyGeneCount=1
    DependencyGenes=Hemogenic
    ExpectedTotalGeneCount=2
    ActualTotalGeneCount=2
    OnlyTargetAndDependencies=True
    GeneDependenciesActive=True
    TargetGeneActive=True
    TriptychGeneSetsMatch=True

无依赖时：

    GeneDependencyChain=None
    DependencyGeneCount=0
    ExpectedTotalGeneCount=1

### 7.2 语义断言

替换当前：

    gene.total_count_is_one
    gene.unique_target_matches

建议使用：

    gene.target_count_is_one
    gene.dependencies_present
    gene.dependencies_active
    gene.target_active
    gene.total_count_matches_expected
    gene.only_target_and_dependencies
    gene.triptych_gene_sets_match
    gene.visual_signature_changed

其中 `gene.visual_signature_changed` 只对 passed 的外观 Gene 要求为 true。

`no_visual_effect` 跳过用例应记录依赖信息，但不伪造视觉变化断言成功。

## 8. Evidence 多行 JSON 实施方案

### 8.1 推荐实现

继续使用 `DataContractJsonSerializer`，不要更换序列化模型。

推荐通过 `JsonReaderWriterFactory.CreateJsonWriter` 创建支持缩进的 JSON writer，例如：

    DataContractJsonSerializer serializer =
        new DataContractJsonSerializer(typeof(AutoTestEvidenceManifest));

    using (FileStream stream = new FileStream(...))
    using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(
        stream,
        Encoding.UTF8,
        ownsStream: false,
        indent: true,
        indentChars: "  "))
    {
        serializer.WriteObject(writer, manifest);
        writer.Flush();
    }

具体重载以 .NET Framework 4.7.2 实际可用签名为准。

需要增加命名空间：

    System.Text
    System.Xml

优点：

- 继续使用同一 DataContract 模型；
- 不引入 Newtonsoft.Json；
- 不改变日期格式；
- 不经过 Dictionary 或 JObject 二次转换；
- 避免字段丢失和类型漂移；
- Read 方法可保持不变。

### 8.2 不推荐方案

不建议为了缩进引入 Newtonsoft.Json，原因：

- 当前 AutoTestProject 没有 Newtonsoft 引用；
- 会新增程序集依赖和部署风险；
- 默认日期和 DataContract 行为可能变化；
- 只为排版不值得扩大依赖面。

不建议使用字符串级简单替换：

    json.Replace(",", ",\n")

因为会破坏：

- 字符串内部逗号；
- 转义字符；
- 嵌套对象；
- 日志消息；
- JSON 合法性。

如果目标框架上的缩进 writer 不可用，备用方案必须是一个真正理解 JSON 字符串、转义和嵌套层级的流式格式化器，并配套完整测试。

### 8.3 输出格式要求

建议：

- 两个空格缩进；
- UTF-8；
- 顶层 `{` 单独一行；
- 每个对象字段独立成行；
- 数组元素按层级展开；
- 文件末尾允许一个换行；
- 不要求属性重新排序。

示例：

    {
      "schemaVersion": 1,
      "runId": "...",
      "operations": [
        {
          "operationId": "...",
          "success": true
        }
      ]
    }

### 8.4 路径和目录不变

Evidence 仍位于：

    <Base>_Screenshots/<Base>_Evidence.json

Result 仍位于外层：

    <Base>_Result.json

Result 中的 `evidencePath` 不变。

归档保留和删除逻辑不需要因换行调整。

## 9. 需要修改的主要文件

### 9.1 Gene 依赖

- `source/AutoTestProject/AutoTestThreeViewService.cs`
  - 解析 prerequisite 闭包；
  - 准备依赖基线；
  - 应用目标 Gene；
  - 正式 Pawn 集合校验；
  - AppearanceTemplate 和 SyncGenes；
  - Result Context 字段。

- `source/AutoTestProject/AutoTestRunner.ThreeViewModule.cs`
  - Notes 字段；
  - 失败条件；
  - 停止断言总数恒等于 1。

- `source/AutoTestProject/Integration/AutoTestRunner.SemanticModule.cs`
  - 新 Gene dependency 语义断言；
  - 替换 `gene.total_count_is_one`。

- 可能涉及 `source/AutoTestProject/AutoTestModels.cs`
  - 如果依赖信息需要进入通用 Result/Manifest 模型。

### 9.2 Evidence 多行 JSON

- `source/AutoTestProject/Core/AutoTestEvidenceBuilder.cs`
  - 修改 `AutoTestEvidenceSerializer.Write`。

- `AutoTest.Core.Tests/Program.cs`
  - 增加格式与往返测试。

- `RimDevTools.Tests/Program.cs`
  - 验证 CLI Evidence 读取器接受多行 JSON；
  - 防止校验逻辑错误依赖单行格式。

## 10. 测试方案

### 10.1 Evidence 单元测试

扩展 `TestEvidenceRoundTrip` 或新增测试：

1. 写入 Evidence；
2. 断言文件包含换行；
3. 断言顶层字段不是全部位于同一行；
4. 断言至少有缩进字段，例如两个空格加字段名；
5. 使用 `AutoTestEvidenceSerializer.Read` 读取；
6. 对比 SchemaVersion、RunId、Assertions、Operations、Logs 等关键数量；
7. 在日志消息中加入逗号、引号、反斜杠和换行，确认没有被格式化破坏；
8. 确认中文内容正常；
9. 确认日期仍可往返读取。

建议增加测试名称：

    evidence JSON is indented and round trips
    evidence JSON formatting preserves escaped strings

### 10.2 RimDevTools 兼容测试

准备一个多行 Evidence fixture，验证：

- passing Evidence 被接受；
- failed Evidence 仍被拒绝；
- mismatched runId 仍被拒绝；
- schemaVersion 校验不变；
- CLI 不依赖 Evidence 为单行。

### 10.3 Gene 无依赖样本

继续使用：

    Ears_Pig

预期：

    DependencyGeneCount=0
    ExpectedTotalGeneCount=1
    TargetGeneCount=1
    OnlyTargetAndDependencies=True
    GeneThreeView=passed

### 10.4 Gene 单层依赖样本

使用：

    Deathrest -> Hemogenic

预期：

    DependencyGenes=Hemogenic
    DependencyGeneCount=1
    ExpectedTotalGeneCount=2
    TargetGeneCount=1
    OnlyTargetAndDependencies=True
    GeneInfoCard=passed
    GeneThreeView=skipped/no_visual_effect

必须确认 Deathrest 探测时 Pawn 真实同时持有：

    Hemogenic
    Deathrest

### 10.5 另一个 Hemogenic 依赖样本

建议再测试一个：

    HemogenDrain -> Hemogenic

或：

    PiercingSpine -> Hemogenic

用于确认 dependency 逻辑不是只针对 Deathrest 特判。

### 10.6 多层依赖测试

原版可能缺少适合的多层链样本，可以在纯单元测试中构造三个 GeneDef：

    A <- B <- C

验证输出顺序：

    A, B

以及最终集合：

    A, B, C

### 10.7 循环依赖测试

构造：

    A.prerequisite = B
    B.prerequisite = A

预期：

    gene_dependency_cycle

不得死循环或栈溢出。

### 10.8 最终实机测试

至少执行：

1. Biotech `Ears_Pig` View；
2. Biotech `Deathrest` UI + View；
3. Biotech `HemogenDrain` UI + View；
4. 一个目标模组无依赖、无视觉 Gene；
5. 截图开启；
6. 检查外层 Result 与内层多行 Evidence。

## 11. 验收标准

### 11.1 Gene 依赖

- `GeneDef.prerequisite` 被递归解析；
- Deathrest 测试 Pawn 同时拥有 Hemogenic；
- Before 签名只包含依赖闭包；
- After 签名包含依赖闭包加目标；
- 目标 Gene 始终恰好一份；
- 无依赖 Gene 的总数仍为 1；
- 有依赖 Gene 的总数等于依赖数加 1；
- 不存在无关 Gene；
- 三个正式 Pawn 的 Gene 集合完全一致；
- Deathrest 无视觉时明确跳过，不伪造 passed；
- GeneInfoCard 保持全量执行；
- XenotypeThreeView 不因本次修改退化。

### 11.2 Evidence 多行 JSON

- Evidence 顶层和嵌套对象多行展开；
- AI 可以按字段、对象和数组分段读取；
- JSON 合法；
- SchemaVersion 不变；
- Read 往返成功；
- 中文、引号、反斜杠、逗号和日志换行不损坏；
- RimDevTools 自动测试结果校验通过；
- Result 中 evidencePath 仍有效；
- 发布包不包含本地测试历史；
- 文件体积增加可接受，不影响归档裁剪。

## 12. 实施顺序

### 阶段一：Evidence 多行化

1. 修改 `AutoTestEvidenceSerializer.Write`；
2. 保持 `Read` 不变；
3. 增加缩进与转义单元测试；
4. 运行 AutoTest.Core.Tests；
5. 运行 RimDevTools.Tests。

### 阶段二：Gene 依赖解析

1. 实现 prerequisite 闭包解析；
2. 增加循环检测；
3. 在 Spec 保存依赖和预期集合；
4. 增加纯逻辑测试。

### 阶段三：Gene 探测流程

1. 建立 dependency-only Before 基线；
2. 添加目标 Gene；
3. 比较目标增量外观签名；
4. 验证依赖和目标 Active 状态；
5. 保持无视觉跳过逻辑。

### 阶段四：正式三视图

1. 复制完整预期 Gene 集合；
2. 更新 Context；
3. 更新 Notes；
4. 更新 Evidence 断言；
5. 更新失败原因。

### 阶段五：完整验证与发布

1. 构建 TempProject；
2. 构建 AutoTestProject；
3. 运行 AutoTest.Core.Tests；
4. 运行 RimDevTools.Tests；
5. 部署；
6. 运行 Ears_Pig、Deathrest、HemogenDrain 定向测试；
7. 人工检查多行 Evidence；
8. 重新生成 RimDevTools-Package；
9. 运行 ReleaseTests；
10. 根据项目版本策略决定是否从 1.1.4 升版；
11. 提交代码，不自动 push。

## 13. 风险与注意事项

### 13.1 不要让 prerequisite 污染目标外观差异

这是本次 Gene 修改最重要的原则。

必须比较：

    dependencies-only -> dependencies + target

不能比较：

    zero genes -> dependencies + target

### 13.2 不要继续使用总数等于 1 的错误断言

保留该断言会让所有合法依赖 Gene 必然失败。

应该验证精确集合，而不是固定数量。

### 13.3 不要自动补充反向依赖

测试 Hemogenic 时加入所有血原系 Gene 会失去单目标测试意义。

### 13.4 不要用 JSON 字符串替换实现缩进

Evidence 包含大量日志和转义文本，简单替换极易生成损坏 JSON。

### 13.5 注意 Evidence 文件体积

缩进会增加文件体积，但主要是空格和换行。

除非实测出现明显性能或存储问题，不应为了减小体积牺牲可读性。

### 13.6 保持当前产物结构

本计划不改变已经确认的结构：

    <Base>_Result.json
    <Base>_Screenshots/
      <Base>_Evidence.json
      <Base>_Manifest.json
      <Base>_Report.md

## 14. 本次明确不扩展的内容

- 不把 Result.json 移入 Screenshots；
- 不同时美化所有 JSON 文件；
- 不改变 Evidence SchemaVersion；
- 不引入 Newtonsoft.Json；
- 不猜测未声明的 Gene 隐式依赖；
- 不为 Deathrest 增加完整行为测试；
- 不把 GeneThreeView 改成 Xenotype 测试；
- 不自动加入目标 Gene 的反向依赖；
- 不移除现有 GeneInfoCard 全量测试；
- 不降低三只 Pawn Gene 集合一致性要求。

## 15. 下一对话启动检查清单

下一轮实施前建议先执行：

1. 阅读本文全文；
2. 检查 `git status --short`；
3. 确认提交 `80dd9c1` 存在；
4. 保留用户未跟踪的 `策划案/修改计划.txt`；
5. 不覆盖本轮或用户的其他未提交修改；
6. 先修改 Evidence 序列化并跑单元测试；
7. 再修改 Gene dependency 逻辑；
8. 用 Deathrest 做必要实机验收；
9. 最终发布包必须由正式代码重新生成，不能包含临时测试注入。
