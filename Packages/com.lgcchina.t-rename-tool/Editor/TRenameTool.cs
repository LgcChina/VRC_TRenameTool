using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Text;
using System;
using System.Text.RegularExpressions;
using System.Linq;

#if UNITY_EDITOR
public class TRenameTool : EditorWindow
{
    private List<GameObject> rootObjects = new List<GameObject>();
    private List<GameObject> allObjects = new List<GameObject>();
    private string nameList = "";
    private Vector2 scrollPos, objectListScroll;
    private bool showChildren = true;
    private string translationStatus = "";
    private string customSeparator = "###";

    // 版本信息
    private const string TOOL_NAME = "LGC_TRenameTool_模型菜单翻译";
    private const string VERSION = "1.0.2";
    private const string UPDATE_DATE = "2026-04-09";

    [MenuItem("LGC/LGC_TRenameTool_模型菜单翻译")]
    public static void ShowWindow()
    {
        TRenameTool window = GetWindow<TRenameTool>("LGC_TRenameTool_模型菜单翻译");
        window.minSize = new Vector2(600, 350);
    }

    void OnEnable()
    {
        minSize = new Vector2(900, 650);
    }

    void OnGUI()
    {
        DrawToolbar();

        EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.3f), GUILayout.ExpandHeight(true));
                DrawLeftPanel();
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.48f), GUILayout.ExpandHeight(true));
                DrawCenterPanel();
                EditorGUILayout.EndVertical();

                EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.2f), GUILayout.ExpandHeight(true));
                DrawRightPanel();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        {
            EditorGUILayout.LabelField($"{TOOL_NAME} {VERSION}", EditorStyles.boldLabel, GUILayout.Width(520)/*520，LGC，当然爱自己啊*/);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"更新: {UPDATE_DATE}", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndHorizontal();
        GUILayout.Space(5);
    }

    // ===================== 纯原生 ObjectField 拖拽，无任何多余按钮 =====================
    void DrawLeftPanel()
    {
        EditorGUILayout.LabelField("根物体输入", EditorStyles.boldLabel);
        GUILayout.BeginVertical("box");
        {
            // 原生Unity物体选择框，完美支持：拖拽 + 点选 + 显示
            GameObject newObj = (GameObject)EditorGUILayout.ObjectField(
                "拖拽/选择根物体",
                rootObjects.Count > 0 ? rootObjects[0] : null,
                typeof(GameObject),
                true,
                GUILayout.Height(50)
            );

            // 拖拽/选择物体后自动加载
            if (newObj != null && (rootObjects.Count == 0 || rootObjects[0] != newObj))
            {
                rootObjects.Clear();
                rootObjects.Add(newObj);
                UpdateObjectList();
                ExtractNames();
            }
        }
        GUILayout.EndVertical();
        GUILayout.Space(1);

        // 子物体显示区域（原版原样保留）
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
                            EditorGUILayout.ObjectField(obj, typeof(GameObject), true, GUILayout.Width(120));
                            GUILayout.Label(obj.name, GUILayout.ExpandWidth(true));
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("未添加根物体", MessageType.Info);
        }
    }

    void DrawCenterPanel()
    {
        EditorGUILayout.LabelField("名称编辑区", EditorStyles.boldLabel);

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

        // 100%还原你的原始关键提示
        EditorGUILayout.HelpBox(
            $"1. 分隔符 {customSeparator} 建议是纯符号，翻译软件不要翻译或删除才行\n" +
            $"2. 翻译后即使无空行（如：服装{customSeparator}靴子{customSeparator}袜子（仅限电脑）），仍能正确分割\n" +
            $"3. 若 {customSeparator} 意外丢失，可手动补加，或按空行分隔（兼容降级）\n" +
            $"4. {customSeparator} 不会被应用到物体名称中\n\n" +
            "注意：由于翻译接口不再提供服务，不过，依然可手动复制提取的文本到外站翻译回来应用",
            MessageType.Warning
        );

        float textAreaHeight = position.height * 0.5f;
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, "box", GUILayout.Height(textAreaHeight));
        {
            nameList = EditorGUILayout.TextArea(nameList, GUILayout.ExpandHeight(true));
        }
        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(translationStatus))
        {
            EditorGUILayout.HelpBox(translationStatus,
                translationStatus.Contains("失败") || translationStatus.Contains("错误")
                ? MessageType.Error : MessageType.Info);
        }

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
                wordWrap = true
            });
        }
    }

    void DrawRightPanel()
    {
        EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
        GUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));
        {
            GUILayout.Space(10);
            GUILayout.FlexibleSpace();

            GUI.backgroundColor = Color.white;

            // 提取
            if (GUILayout.Button("提取", GUILayout.Height(50)))
            {
                ExtractNames();
            }
            GUILayout.Space(5);
            // 唯一的清空所有按钮（移除）
            if (GUILayout.Button("移除", GUILayout.Height(50)))
            {
                ClearAll();
            }
            GUILayout.Space(15);

            // 复制/粘贴/清除内容（仅输入框）
            if (GUILayout.Button("复制内容", GUILayout.Height(30)))
            {
                CopyToClipboard();
            }
            GUILayout.Space(5);
            if (GUILayout.Button("粘贴内容", GUILayout.Height(30)))
            {
                PasteFromClipboard();
            }
            GUILayout.Space(5);
            if (GUILayout.Button("清除内容", GUILayout.Height(30)))
            {
                ClearInputOnly();
            }
            GUILayout.Space(15);




            // 应用
            GUI.enabled = allObjects.Count > 0;
            if (GUILayout.Button("应用", GUILayout.Height(50)))
            {
                ApplyRenaming();
            }
            GUI.enabled = true;
            GUILayout.Space(15);

            // 翻译网站
            if (GUILayout.Button("翻译网站", GUILayout.Height(30)))
            {
                ShowTranslationWebsitesMenu();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Space(10);
        }
        GUILayout.EndVertical();
    }

    // 复制粘贴功能
    void CopyToClipboard()
    {
        if (string.IsNullOrEmpty(nameList))
        {
            translationStatus = "复制失败：内容为空！";
            return;
        }
        EditorGUIUtility.systemCopyBuffer = nameList;
        translationStatus = "复制成功：内容已复制到剪切板！";
    }

    void PasteFromClipboard()
    {
        nameList = EditorGUIUtility.systemCopyBuffer;
        translationStatus = "粘贴成功：已加载剪切板内容！";
    }

    // 仅清空输入框
    void ClearInputOnly()
    {
        nameList = "";
        translationStatus = "已清除输入框内容";
    }

    #region 核心功能
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
            sb.Append(allObjects[i].name);
            if (i < allObjects.Count - 1)
            {
                sb.Append(customSeparator + "\n\n");
            }
        }
        nameList = sb.ToString();
        translationStatus = $"已提取 {allObjects.Count} 个名称（分隔符：{customSeparator}）\n提示：可手动复制翻译";
    }

    private string[] SplitNames(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        string[] specialSplitResult = Regex.Split(text, Regex.Escape(customSeparator))
            .Select(part => part.Trim('\n', ' ', '\t'))
            .Where(part => !string.IsNullOrEmpty(part))
            .ToArray();

        if (specialSplitResult.Length == allObjects.Count)
            return specialSplitResult;

        translationStatus = $"警告：{customSeparator} 标记不完整，尝试按空行分割";
        string[] fallbackSplit = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim('\n', ' ', '\t'))
            .ToArray();
        return fallbackSplit;
    }

    int CountNamesInList()
    {
        return string.IsNullOrEmpty(nameList) ? 0 : SplitNames(nameList).Length;
    }
    #endregion

    #region 辅助功能
    void ShowTranslationWebsitesMenu()
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("谷歌翻译"), false, () => OpenTranslationWebsite("google", nameList));
        menu.AddItem(new GUIContent("必应翻译"), false, () => OpenTranslationWebsite("bing", nameList));
        menu.AddItem(new GUIContent("百度翻译"), false, () => OpenTranslationWebsite("baidu", nameList));
        menu.AddItem(new GUIContent("有道翻译"), false, () => OpenTranslationWebsite("youdao", nameList));
        menu.ShowAsContext();
    }

    void OpenTranslationWebsite(string website, string text)
    {
        string url = website switch
        {
            "google" => $"https://translate.google.com/?text={Uri.EscapeDataString(text)}",
            "bing" => "https://www.bing.com/translator",
            "baidu" => "https://fanyi.baidu.com/",
            "youdao" => "https://fanyi.youdao.com/",
            _ => ""
        };
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

    // 右侧移除按钮：清空所有（唯一的清空功能）
    void ClearAll()
    {
        rootObjects.Clear();
        allObjects.Clear();
        nameList = "";
        translationStatus = "已清除所有内容";
    }

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
            EditorUtility.DisplayDialog("数量不匹配",
                $"名称数({newNames.Length})与物体数({allObjects.Count})不匹配！\n请检查分隔符", "确定");
            return;
        }

        Undo.RecordObjects(allObjects.ToArray(), "批量重命名");
        for (int i = 0; i < allObjects.Count; i++)
        {
            allObjects[i].name = newNames[i].Replace(customSeparator, "").Trim();
        }
        AssetDatabase.Refresh();
        translationStatus = $"已应用重命名 ({allObjects.Count}个物体)";
    }
    #endregion
}
#endif
