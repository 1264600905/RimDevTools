# TempProject

RimWorld 1.7 模组开发模板。

首次使用时请修改：

- `About/About.xml` 中的名称、作者和 packageId。
- `mod_config.jsonc` 中的模组名、RimWorld 路径和 Workshop 路径。
- `source/TempProject` 中的程序集名称和命名空间。

常用命令：

```powershell
.\.tools\RimDevTools.exe doctor
.\.tools\RimDevTools.exe deploy --dry-run
.\.tools\RimDevTools.exe test run --mode quick
```
