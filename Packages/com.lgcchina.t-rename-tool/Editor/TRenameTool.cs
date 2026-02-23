using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System;
using System.Text.RegularExpressions;
#if UNITY_EDITOR
public class TRenameTool : EditorWindow
{
    private List<GameObject> rootObjects = new List<GameObject>();
    private List<GameObject> allObjects = new List<GameObject>();
    private string nameList = "";
    private Vector2 scrollPos, objectListScroll;
    private bool showChildren = true;
    private string translationStatus = "";
    private string customSeparator = "###"; // 自定义分隔符

    [MenuItem("LGC/LGC_模型菜单翻译")]
    public static void ShowWindow()
    {
        GetWindow<TRenameTool>("LGC_模型菜单翻译");
    }

    void OnGUI()
    {
        // 计算三个区域的宽度比例
        float leftPanelWidth = position.width * 0.3f;
        float centerPanelWidth = position.width * 0.49f;
        float rightPanelWidth = position.width * 0.2f;
        float spacerWidth = position.width * 0.01f;

        EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
        {
            DrawLeftPanel(leftPanelWidth);
            DrawCenterPanel(centerPanelWidth);
            DrawRightPanel(rightPanelWidth);
            GUILayout.Space(spacerWidth);
        }
        EditorGUILayout.EndHorizontal();
    }

    void DrawLeftPanel(float width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
        GUILayout.Space(5);
        {
            // 物体输入区域
            EditorUtility.SetDirty(this); // 确保界面实时刷新
            EditorGUILayout.LabelField("物体输入", EditorStyles.boldLabel);
            GUILayout.BeginVertical("box");
            {
                // 提示文字强调"符号不被翻译"（无格式示例）
                Rect dragArea = GUILayoutUtility.GetRect(0, 70, GUILayout.ExpandWidth(true));
                GUI.Box(dragArea,
                    "拖拽根物体到这里（自动提取名称）\n" +
                    $"提取后名称间用 {customSeparator} 分隔\n" +
                    "此符号为纯标记，不会被翻译软件处理",
                    EditorStyles.helpBox
                );

                // 拖拽处理（自动提取逻辑不变）
                if (dragArea.Contains(Event.current.mousePosition))
                {
                    if (Event.current.type == EventType.DragUpdated)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.DragPerform)
                    {
                        rootObjects.Clear();
                        foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                        {
                            if (obj is GameObject go) rootObjects.Add(go);
                        }
                        UpdateObjectList();
                        ExtractNames(); // 拖入自动提取
                        Event.current.Use();
                    }
                }

                // 显示已添加的根物体
                if (rootObjects.Count > 0)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.LabelField("已添加的根物体:", EditorStyles.miniBoldLabel);
                    foreach (var obj in rootObjects)
                    {
                        EditorGUILayout.ObjectField(obj, typeof(GameObject), true);
                    }
                }
            }
            GUILayout.EndVertical();
            GUILayout.Space(10);

            // 子物体显示区域
            if (allObjects.Count > 0)
            {
                EditorGUILayout.LabelField("所有子级物体", EditorStyles.boldLabel);
                showChildren = EditorGUILayout.Foldout(showChildren, $"显示子物体 ({allObjects.Count}个)", true);
                if (showChildren)
                {
                    objectListScroll = EditorGUILayout.BeginScrollView(objectListScroll, "box", GUILayout.ExpandHeight(true));
                    {
                        foreach (var obj in allObjects)
                        {
                            EditorGUILayout.BeginHorizontal();
                            {
                                EditorGUILayout.ObjectField(obj, typeof(GameObject), true);
                                GUILayout.Label(obj.name, GUILayout.Width(150));
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("没有添加任何物体", MessageType.Info);
            }
        }
        GUILayout.EndVertical();
    }

    void DrawCenterPanel(float width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
        GUILayout.Space(5);
        {
            // 名称编辑区域
            EditorGUILayout.LabelField("名称编辑区", EditorStyles.boldLabel);

            // 自定义分隔符输入
            GUILayout.BeginVertical("box");
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("自定义分隔符:", GUILayout.Width(100));
                    string newSeparator = EditorGUILayout.TextField(customSeparator, GUILayout.ExpandWidth(true));
                    if (newSeparator != customSeparator && !string.IsNullOrEmpty(newSeparator))
                    {
                        customSeparator = newSeparator;
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Space(10);

            // 【关键提示】移除提取后格式示例，仅保留核心说明
            EditorGUILayout.HelpBox(
                $"1. 分隔符 {customSeparator} 建议是纯符号，翻译软件不要翻译或删除才行\n" +
                $"2. 翻译后即使无空行（如：服装{customSeparator}靴子{customSeparator}袜子（仅限电脑）），仍能正确分割\n" +
                $"3. 若 {customSeparator} 意外丢失，可手动补加，或按空行分隔（兼容降级）\n\n" +
                "注意：翻译接口不再提供服务，可手动复制提取的文本到外站翻译回来",
                MessageType.Warning
            );

            // 名称列表编辑区（逻辑不变）
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, "box", GUILayout.ExpandHeight(true));
            {
                nameList = EditorGUILayout.TextArea(nameList, GUILayout.ExpandHeight(true));
            }
            EditorGUILayout.EndScrollView();

            // 翻译状态显示（逻辑不变）
            if (!string.IsNullOrEmpty(translationStatus))
            {
                EditorGUILayout.HelpBox(translationStatus,
                    string.IsNullOrEmpty(translationStatus) ? MessageType.None :
                    translationStatus.Contains("失败") || translationStatus.Contains("错误") ? MessageType.Error : MessageType.Info);
            }

            // 名称数量匹配状态（精准识别分隔符分割）
            if (allObjects.Count > 0)
            {
                int nameCount = CountNamesInList();
                string status = nameCount == allObjects.Count
                    ? $"<color=green>名称数量匹配: {nameCount}/{allObjects.Count}</color>"
                    : $"<color=red>名称数量不匹配: {nameCount}/{allObjects.Count}</color>\n" +
                      $"<color=red>请检查 {customSeparator} 是否完整保留</color>";
                EditorGUILayout.LabelField(status, new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 5, 5),
                    wordWrap = true
                });
            }
        }
        GUILayout.EndVertical();
    }

    void DrawRightPanel(float width)
    {
        GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.ExpandHeight(true));
        {
            GUILayout.Space(5);
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
            GUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
            {
                GUILayout.Space(10);
                GUILayout.FlexibleSpace();

                // 提取按钮（保留，用于重新提取）
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("提取", GUILayout.Height(50)))
                {
                    ExtractNames();
                }
                GUILayout.Space(15);

                // 清除按钮（逻辑不变）
                if (GUILayout.Button("清除", GUILayout.Height(50)))
                {
                    ClearAll();
                }
                GUILayout.Space(15);

                // 应用按钮（逻辑不变，依赖分隔符分割）
                GUI.enabled = allObjects.Count > 0;
                if (GUILayout.Button("应用", GUILayout.Height(50)))
                {
                    ApplyRenaming();
                }
                GUI.enabled = true;
                GUILayout.Space(15);

                // 翻译网站按钮（逻辑不变）
                if (GUILayout.Button("翻译网站", GUILayout.Height(30)))
                {
                    ShowTranslationWebsitesMenu();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Space(10);
            }
            GUILayout.EndVertical();
            GUILayout.Space(5);
        }
        GUILayout.EndVertical();
    }

    // ------------------------------ 核心逻辑：自定义分隔符分割 + 保留名称内换行 ------------------------------
    #region 核心功能优化（自定义分隔符方案 + 换行保留）
    // 【优化1】提取名称：保留原始名称内的换行，仅添加分隔符
    void ExtractNames()
    {
        if (allObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("提示", "没有物体可提取名称", "确定");
            return;
        }
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < allObjects.Count; i++)
        {
            // 直接保留原始名称（含内部换行）
            sb.Append(allObjects[i].name);
            // 每个名称后添加分隔符（最后一个名称不加，避免多余标记）
            if (i < allObjects.Count - 1)
            {
                sb.Append(customSeparator + "\n\n");
            }
        }
        nameList = sb.ToString();
        translationStatus = $"已提取 {allObjects.Count} 个名称（分隔符：{customSeparator}）\n" +
                           "提示：可手动复制文本到外部翻译网站翻译后粘贴回来";
    }

    // 【优化2】分割名称：保留名称内换行，仅清理分隔符残留的前后空行/空格
    private string[] SplitNames(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];

        // 步骤1：统一换行符（处理Windows/macOS差异，保留名称内换行）
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 步骤2：按自定义分隔符分割（纯符号匹配，不破坏名称内换行）
        string[] specialSplitResult = Regex.Split(text, Regex.Escape(customSeparator), RegexOptions.None)
            .Select(part =>
            {
                // 仅清理分割后残留的"前后空行/空格/制表符"，保留名称内部换行
                string cleanPart = part.Trim('\n', ' ', '\t');
                // 仅清理"多余空格"（不处理换行），避免名称内空格重复
                cleanPart = Regex.Replace(cleanPart, @" +", " ");
                return cleanPart;
            })
            .Where(part => !string.IsNullOrEmpty(part)) // 过滤空内容
            .ToArray();

        // 情况1：分隔符分割结果匹配物体数 → 精准返回（含名称内换行）
        if (specialSplitResult.Length == allObjects.Count)
        {
            return specialSplitResult;
        }

        // 情况2：分隔符失效（意外丢失），降级按空行分割（仍保留名称内换行）
        translationStatus = $"警告：{customSeparator} 标记不完整，尝试按空行分割";
        string[] fallbackSplit = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                string cleanPart = part.Trim('\n', ' ', '\t');
                cleanPart = Regex.Replace(cleanPart, @" +", " ");
                return cleanPart;
            })
            .ToArray();
        return fallbackSplit;
    }

    // 【优化3】计数逻辑：依赖分隔符分割结果，精准计数
    int CountNamesInList()
    {
        if (string.IsNullOrEmpty(nameList)) return 0;
        return SplitNames(nameList).Length;
    }
    #endregion

    // ------------------------------ 原有辅助功能（同步优化提示文本） ------------------------------
    #region 原有辅助功能
    void ShowTranslationWebsitesMenu()
    {
        GenericMenu menu = new GenericMenu();
        string selectedText = nameList;
        menu.AddItem(new GUIContent("谷歌翻译"), false, () => OpenTranslationWebsite("google", selectedText));
        menu.AddItem(new GUIContent("必应翻译"), false, () => OpenTranslationWebsite("bing", selectedText));
        menu.AddItem(new GUIContent("百度翻译"), false, () => OpenTranslationWebsite("baidu", selectedText));
        menu.AddItem(new GUIContent("有道翻译"), false, () => OpenTranslationWebsite("youdao", selectedText));
        menu.ShowAsContext();
    }

    void OpenTranslationWebsite(string website, string text)
    {
        string url = "";
        switch (website)
        {
            case "google":
                url = "https://translate.google.com/";
                if (!string.IsNullOrEmpty(text))
                    url += "?text=" + Uri.EscapeDataString(text);
                break;
            case "bing": url = "https://www.bing.com/translator"; break;
            case "baidu": url = "https://fanyi.baidu.com/"; break;
            case "youdao": url = "https://fanyi.youdao.com/"; break;
        }
        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
    }

    void UpdateObjectList()
    {
        allObjects.Clear();
        foreach (var root in rootObjects)
            CollectChildrenRecursive(root.transform);
    }

    void CollectChildrenRecursive(Transform parent)
    {
        allObjects.Add(parent.gameObject);
        for (int i = 0; i < parent.childCount; i++)
            CollectChildrenRecursive(parent.GetChild(i));
    }

    void ClearAll()
    {
        if (EditorUtility.DisplayDialog("确认清除", "确定要清除所有物体和名称列表吗？", "确定", "取消"))
        {
            rootObjects.Clear();
            allObjects.Clear();
            nameList = "";
            translationStatus = "已清除所有内容";
        }
    }

    // 【优化4】应用重命名：移除格式示例，保留名称内换行
    void ApplyRenaming()
    {
        if (allObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("错误", "没有物体可重命名!", "确定");
            return;
        }

        string[] newNames = SplitNames(nameList);
        if (newNames.Length != allObjects.Count)
        {
            EditorUtility.DisplayDialog(
                "数量不匹配",
                $"输入的名称数({newNames.Length})与物体数({allObjects.Count})不匹配!\n\n" +
                $"请确保名称间有 {customSeparator} 分隔",
                "确定"
            );
            return;
        }

        Undo.RecordObjects(allObjects.ToArray(), "批量重命名");
        for (int i = 0; i < allObjects.Count; i++)
        {
            // 直接应用含换行的名称（翻译结果有换行则保留，无则不添加）
            allObjects[i].name = newNames[i];
        }
        Debug.Log($"成功重命名 {allObjects.Count} 个物体");
        AssetDatabase.Refresh();
        translationStatus = $"已应用重命名 ({allObjects.Count}个物体)";
    }
    #endregion
}
#endif
