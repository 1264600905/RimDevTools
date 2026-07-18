# RimWorld 通用 Shader 使用指南

本文档总结 RimWorld 模组中使用自定义 Shader 的稳定做法，刻意不绑定某个具体项目、目录名或 bundle 名，适合作为后续所有项目的通用参考。

## 1. 先说结论

RimWorld 模组中要使用自定义 Shader，稳定流程是：

1. 在独立 Unity 工程中编写 `.shader`
2. 通过 Unity 的 `BuildPipeline.BuildAssetBundles(...)` 打包为 AssetBundle
3. 将 bundle 放到模组目录的 `AssetBundles/`
4. 在 C# 中通过 `ContentFinder<Shader>.Get(...)` 加载 Shader
5. 创建 `Material`
6. 用 `MaterialPropertyBlock` 传参
7. 用 `Graphics.DrawMesh(...)` 在合适的渲染时机绘制

重点是：**RimWorld 不会直接读取你源码目录里的 `.shader` 文件，必须先打包成 AssetBundle。**

## 2. 推荐目录结构

```text
你的模组项目/
├── AssetBundles/
├── 1.6/
├── UnityProjects/
│   └── ShaderBundleBuilder/
│       ├── Assets/
│       │   ├── Data/
│       │   │   └── <ModFolderName>/
│       │   │       └── Materials/
│       │   └── Editor/
│       └── ProjectSettings/
└── source/
    └── <YourMod>/
```

最重要的稳定规则：

- Shader 文件放在：
  `Assets/Data/<ModFolderName>/Materials/...`
- 打包产物放在：
  `AssetBundles/`

## 3. 打包链路

推荐通过脚本以批处理模式调用 Unity：

```powershell
Unity.exe -batchmode -nographics -quit -projectPath <UnityProjectPath> -executeMethod <BuildMethod>
```

并把这个步骤接入主部署脚本，而不是完全手工运行。

## 4. RimWorld 实际怎么找 Shader

运行时一般这样加载：

```csharp
Shader shader = ContentFinder<Shader>.Get("CTGAS/NanoShield_Enhanced", false);
```

RimWorld 会按下面规则在 bundle 内查找：

```text
Assets/Data/<ModFolderName>/Materials/<itemPath>.shader
```

所以如果 C# 里写的是：

```csharp
ContentFinder<Shader>.Get("Effects/ShieldA", false)
```

那么 bundle 内必须能匹配到：

```text
Assets/Data/<ModFolderName>/Materials/Effects/ShieldA.shader
```

这意味着：

1. 文件必须在 `Materials/` 目录树下
2. 文件路径尾部必须和 `ContentFinder<Shader>.Get("...")` 参数一致
3. shader 源码里的 `Shader "..."` 名字也应该和查找名保持一致

## 5. 推荐运行时结构

- `HediffComp` / `ThingComp` / `MapComponent`
  - 保存状态
- `Drawer`
  - 只负责画
- Harmony Patch
  - 只负责把 `Draw()` 挂到正确渲染时机

## 6. 最常见的两类错误

### 6.1 找不到 Shader

优先排查：

1. 文件是否放在 `Assets/Data/<ModFolderName>/Materials/...`
2. 文件名是否和 `ContentFinder<Shader>.Get("...")` 参数对应
3. bundle 是否重新打包
4. bundle 是否已同步到游戏目录

### 6.2 粉色方块

粉色通常表示：

- shader 已加载到
- 但 shader 本体编译失败或 GPU/平台不支持

优先排查：

1. `#pragma target` 是否过高
2. 是否缺少 `FallBack`
3. 是否用了当前环境不兼容的函数
4. shader 本体是否被“简化改坏”

## 7. 已验证有效的兼容经验

如果 shader 在游戏里表现为粉色，先尝试：

1. `#pragma target 3.0` 降到 `2.0`
2. 增加：
   `FallBack "Transparent/VertexLit"`
3. 必要时把 `frac(x)` 改成：
   `x - floor(x)`

## 8. 一句话总结

真正值得模板化的是：

- 独立 Unity 打包
- AssetBundles 自动部署
- `ContentFinder<Shader>` 的正确路径规则
- `MaterialPropertyBlock + Graphics.DrawMesh` 渲染链路

不要急着自己重写一个“更简单的 shader 效果”来代替已经验证过的版本。
