# LGC_TRenameTool_模型菜单翻译

当前版本：`2.1.1`  
更新日期：`2026-11-26`

这是一个 Unity Editor 工具，用于提取 VRChat Avatar 菜单按钮文字。工具本身不提供机器翻译：它会生成带稳定 ID 的文本，由用户复制到任意翻译网页，再将译文粘贴回来校验、预览并应用。

## 支持范围

- 原生 `VRCExpressionsMenu`
  - Control 名称
  - Puppet Label 名称
  - 递归子菜单
- Modular Avatar
  - `ModularAvatarMenuItem`
  - `ModularAvatarMenuInstaller` 引用的菜单
  - Menu Item 引用的原生菜单与 Puppet Label
- VRCFury
  - Toggle 与 Puppet 的菜单路径
  - Full Controller 引用菜单及菜单前缀
  - Move Menu Item、Reorder Menu Item
  - SPS Options 与旧版 World Constraint 菜单路径
  - 新版 `content` 和旧版 `config.features` 序列化格式

工具不会翻译参数名、Animator 状态、动画绑定路径、对象引用、GUID 或 VRCFury Exclusive Tag。

## 安装

将整个 `com.lgcchina.t-rename-tool` 文件夹放到 Unity 项目的 `Packages` 目录，或通过包含该包的 VPM/VCC 仓库安装。包名必须保持：

```text
com.lgcchina.t-rename-tool
```

要求 Unity `2022.3` 和 VRChat Avatars SDK `3.x`。Modular Avatar 与 VRCFury 是可选支持项，没有安装时不会阻止原生 VRC 菜单功能使用。

## 使用方法

1. 在 Unity 菜单打开 `LGC/LGC_TRenameTool_模型菜单翻译`。
2. 将 Avatar 或某个需要处理的子物体拖入“待翻译物体”。也可以额外拖入一个 `Assets/` 下的原生 `VRCExpressionsMenu`。
3. 点击“扫描待翻译按钮”。
4. 在“找到的菜单来源”中检查物体或资源路径，取消不需要翻译的来源。使用“定位”可在 Hierarchy 或 Project 中找到来源。
5. 点击“复制待翻译文本”，粘贴到翻译网页。
6. 将翻译结果复制回来，点击“从剪贴板粘贴译文”或手动粘贴。按钮会把完整剪贴板内容显示在右侧译文框中；即使解析失败，文本也会保留供修正。
7. 点击“解析并更新预览”。
8. 在预览中检查 `MA`、`VRCFury`、`VRC` 类型、原名、译名和路径，并取消不希望修改的条目。
9. 点击窗口底部固定的“应用所选修改”。
10. 在 Inspector、菜单预览或 Play Mode 中确认结果，再自行保存场景或资源。

切换菜单来源会改变导出条目的数量和编号。如果已粘贴译文，工具会要求确认并清空译文，然后重新生成待翻译文本，防止译文错配。

## 稳定 ID 格式

导出文本示例：

```text
ID0001  Clothing
ID0002  Jacket
ID0003  Color
```

ID 仅用于回填定位，不会显示在修改预览中，也不会写入菜单。解析器兼容常见翻译网页改写：

```text
ID0001: 服装
[ID0002] 夹克
ID 0003：颜色
```

解析时会清理 Markdown 代码围栏、零宽字符、不间断空格、HTML 实体、列表符号和常见引号。如果所有 ID 均丢失，只有在非空行数量与待翻译条目完全一致时，才会按顺序降级匹配。

不要让翻译网页翻译或删除 `ID0001` 一类编号。编号缺失、重复、出现未知编号或译文为空时，工具会阻止应用。

`2.1.0` 及以上统一使用 `ID0001` 格式；当前工具不兼容实验阶段旧版的 `LGC0001` 导入文本，请重新扫描并复制新文本。

`2.1.1` 修复了清空译文时文本框可能因仍处于编辑焦点而恢复旧内容的问题。点击“清空译文”后，右侧译文框会清空并失去焦点。

## 菜单来源分组

- MA 与 VRCFury 条目按其所在场景物体分组。
- 原生 VRC 菜单按菜单资源分组。
- 每组显示类型、条目数量、Hierarchy 路径或资源路径，并提供“定位”按钮。
- 关闭某个来源后，该来源不会进入导出文本、译文校验、修改预览或最终应用。
- 同一个物体同时包含多种菜单组件时，会合并为一个来源，类型显示为组合类型。

## VRCFury 菜单路径

VRCFury 经常使用以下形式描述菜单层级：

```text
Clothing/Jacket/Color
```

工具会把三个层级分别导出，应用时再用稳定的 `/` 重新拼接。路径片段译文不能包含 `/` 或 `\`，否则会创建错误的菜单层级，因此校验会阻止应用。

## Modular Avatar 注意事项

MA Menu Item 的显示名称规则是：`label` 非空时使用 `label`，否则使用 GameObject 名称。

当原始 `label` 为空时，本工具会把译文写入 `label`，不会重命名 GameObject。这样可以避免影响模型层级、动画路径和其他插件对稳定对象名称的引用。

## Package 资源、Assets 资源与 Prefab

- 工具不支持修改 `Packages/` 下的原生菜单资源；扫描到时会跳过并显示警告。
- 场景中的 Package Prefab 实例仍可通过属性覆盖保存 MA/VRCFury 译文，因为这不会修改包源文件。
- `Assets/` 下的原生菜单资源可以修改。被多个 Avatar 共用的菜单资产只会扫描一次，修改会影响所有引用它的 Avatar。
- 如果只希望修改当前场景中的组件，请不要勾选额外出现的 `Assets/` 菜单资源来源。

## Undo、保存与安全检查

- 应用使用 Unity Undo，可撤销本次修改。
- 工具不会自动保存场景、Prefab 或资源。
- 如果扫描后目标属性被别的工具修改，应用会取消并要求重新扫描。
- 译文校验后再次手动编辑文本会使校验立即失效，必须重新解析后才能应用。
- 目标丢失、属性不存在、Package 资源、译文校验失败等情况会在写入前阻止应用，不进行部分写入。
- 参数、动画、预制件和 GameObject 等运行资源名称保持稳定；版本号只用于工具窗口、脚本注释和文档。

## 常见问题

### 扫描不到菜单

- 确认拖入的是包含 MA/VRCFury 组件的物体或其父物体。
- 确认“包含未激活物体”符合模型结构。
- 确认 MA/VRCFury 组件没有 Missing Script。
- 如果只翻译原生菜单，可直接拖到“额外原生菜单”。
- Avatar Descriptor 的 Expressions Menu 为空时，原生菜单不会从 Descriptor 被发现，但 MA/VRCFury 仍会单独扫描。
- `Packages/` 下的原生菜单会被安全跳过。

### 翻译网页删除了 ID

优先手动补回 ID。工具虽然提供按非空行顺序的降级匹配，但只有条目数量完全一致时才启用，并且应用前必须仔细检查预览。

### 应用后看不到变化

- MA Menu Item 的译文通常写入组件 `label`，GameObject 名称会保持原样。
- 原生菜单资产可能被多个位置引用，请确认查看的是同一个资源。
- 工具不会自动保存；确认结果后需要由用户保存场景或资源。

### 为什么不翻译参数名称

参数名与 Animator、动画、同步逻辑和第三方组件相互引用。翻译它们通常会破坏功能，所以本工具只处理玩家可见的菜单文字。
