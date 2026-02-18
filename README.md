# RimWorld Mod Template

这是一个专门为AI开发者设计的RimWorld模组模板工程，用于快速创建和开发RimWorld模组。

## 项目结构

```
RimWorld Mod Template/
├── About/                          # 模组基本信息和描述
├── CE_Patches/                     # Combat Extended兼容性补丁
├── Design_Documents/              # 设计文档和策划资料（原"策划案"）
├── Languages/                      # 多语言支持文件
├── LoadFolders.xml                # 版本控制加载配置
├── MuzzleFlash/                   # 枪口闪光效果
├── Reference_Documentation/       # 参考文档和示例代码
├── Textures/                      # 贴图资源
└── source/                       # C#源代码
    └── TempProject/              # 主要模组项目
```

## 重要路径说明

### 开发相关路径
- **`source/TempProject/`** - 主要的C#模组代码存放位置
  - `TempProject.csproj` - 项目文件
  - `TempProject.sln` - 解决方案文件
  - 包含模组的主要逻辑类文件

### 配置文件
- **`LoadFolders.xml`** - 控制不同游戏版本的文件夹加载
  - 支持1.5和1.6版本的模组加载配置
  - 包含Combat Extended和MuzzleFlash的条件加载

### 多语言支持
- **`Languages/`** - 存放各语言翻译文件
  - `ChineseSimplified/` - 简体中文
  - `ChineseTraditional/` - 繁体中文
  - `English/` - 英文

### 资源文件
- **`Textures/`** - 存放模组的贴图资源
- **`About/`** - 模组描述、版本信息等

## 参考文档说明

### Reference_Documentation 目录
这是最重要的参考资源，包含各种模组开发的示例代码：


## 开发指南

### 1. 项目设置
- 主要C#代码位于 `source/TempProject/`
- 使用Visual Studio或Rider进行开发
- 需要安装.NET框架和RimWorld开发环境

### 2. 编译说明
编译时出现的以下错误是正常的，因为缺少游戏依赖：
```
error CS0246: 未能找到类型或命名空间名"UnityEngine"
error CS0246: 未能找到类型或命名空间名"Verse"
```

### 3. 参考学习
- 查看 `Reference_Documentation/` 下的示例代码
- 重点学习 `rimworld-XML-Source/` 中的教程文档
- 参考各个子目录中的完整实现案例

### 4. 文件命名规范
- XML定义文件使用英文命名
- C#类文件遵循C#命名约定
- 资源文件使用描述性英文名称

## 版本兼容性
- 支持RimWorld 1.5和1.6版本
- 兼容Combat Extended模组
- 支持MuzzleFlash模组

## 注意事项
- 本模板专为AI开发者设计，包含丰富的学习资源
- 所有示例代码都是完整可用的实现
- 参考文档包含中英双语说明，便于理解
- 开发时请遵循RimWorld模组开发规范

## 快速开始
1. 克隆或下载此模板
2. 重命名 `TempProject` 为你的模组名称
3. 修改 `About/About.xml` 中的模组信息
4. 开始在 `source/` 目录下编写你的模组代码
5. 参考示例文档学习具体的实现方法

祝模组开发顺利！

作者提示： 注意使用rimworld-source mcp工具进行开发！
注意阅读规则文件 D:\RimworldProject\模组模板\模组模板\.cursor\rules\rule.mdc
如果正式开发，记得把TempProject重新命名为标准的模组名称！

记得在第一次部署前检查模组文件，并且删除可能被部署的不必要的文件，
注意这里的不必要的文件排除 
.claude    .cursor   .gemini   参考文档  策划案   以及不在部署文件范围的其他文件。

