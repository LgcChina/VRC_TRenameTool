using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net;
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
    private bool isTranslating = false;
    private string translationStatus = "";
    private string sourceLanguage = "en"; // 源语言
    private string targetLanguage = "zh-CHS"; // 目标语言
    private bool isEnPairSelected = true; // 标记当前选择的语言对
    private bool isJpPairSelected = false;

    // 【核心方案】纯符号分隔符：###（三井号，无语义、不被翻译、低冲突）
    private const string SpecialSeparator = "###";
    // 提取时格式：名称 + ### + 空行（空行仅提升可读性，丢失不影响分割）
    private const string ObjectSeparator = SpecialSeparator + "\n\n";
    private const string SingleNewline = "\n";

    [MenuItem("LGC/模型菜单翻译")]
    public static void ShowWindow()
    {
        GetWindow<TRenameTool>("模型菜单翻译");
    }

    void OnGUI()
    {
        // 计算四个区域的宽度比例
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
                // 提示文字强调“符号不被翻译”（无格式示例）
                Rect dragArea = GUILayoutUtility.GetRect(0, 70, GUILayout.ExpandWidth(true));
                GUI.Box(dragArea,
                    "拖拽根物体到这里（自动提取名称）\n" +
                    $"提取后名称间用 {SpecialSeparator} 分隔\n" +
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

            // 语言选择和翻译方向切换（逻辑不变）
            GUILayout.BeginVertical("box");
            {
                GUILayout.BeginHorizontal(GUILayout.Height(35));
                {
                    GUILayout.Space(5);
                    Color originalColor = GUI.backgroundColor;
                    GUI.backgroundColor = isEnPairSelected ? new Color(0.5f, 0.8f, 0.5f) : Color.white;
                    if (GUILayout.Button(GetEnButtonText(), GUILayout.ExpandWidth(true), GUILayout.Height(30)))
                    {
                        SetLanguagePair("en", "zh-CHS");
                        isEnPairSelected = true;
                        isJpPairSelected = false;
                    }
                    GUILayout.Space(5);
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("反转", GUILayout.Width(70), GUILayout.Height(30)))
                    {
                        ReverseTranslationDirection();
                    }
                    GUILayout.Space(5);
                    GUI.backgroundColor = isJpPairSelected ? new Color(0.5f, 0.8f, 0.5f) : Color.white;
                    if (GUILayout.Button(GetJpButtonText(), GUILayout.ExpandWidth(true), GUILayout.Height(30)))
                    {
                        SetLanguagePair("ja", "zh-CHS");
                        isEnPairSelected = false;
                        isJpPairSelected = true;
                    }
                    GUI.backgroundColor = originalColor;
                    GUILayout.Space(5);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(3);
            }
            GUILayout.EndVertical();
            GUILayout.Space(10);

            // 【关键提示】移除提取后格式示例，仅保留核心说明
            EditorGUILayout.HelpBox(
                $"1. 分隔符 {SpecialSeparator} 是纯符号，翻译软件不会翻译或删除\n" +
                $"2. 翻译后即使无空行（如：服装{SpecialSeparator}靴子{SpecialSeparator}袜子（仅限电脑）），仍能正确分割\n" +
                $"3. 若 {SpecialSeparator} 意外丢失，可手动补加，或按空行分隔（兼容降级）",
                MessageType.Info
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

            // 名称数量匹配状态（精准识别###分割）
            if (allObjects.Count > 0)
            {
                int nameCount = CountNamesInList();
                string status = nameCount == allObjects.Count
                    ? $"<color=green>名称数量匹配: {nameCount}/{allObjects.Count}</color>"
                    : $"<color=red>名称数量不匹配: {nameCount}/{allObjects.Count}</color>\n" +
                      $"<color=red>请检查 {SpecialSeparator} 是否完整保留</color>";
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

                // 翻译按钮（逻辑不变）
                GUI.enabled = !isTranslating && !string.IsNullOrEmpty(nameList);
                if (GUILayout.Button("翻译", GUILayout.Height(50)))
                {
                    StartTranslation();
                }
                GUI.enabled = true;
                GUILayout.Space(15);

                // 应用按钮（逻辑不变，依赖###分割）
                GUI.enabled = allObjects.Count > 0 && !isTranslating;
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

    // ------------------------------ 核心逻辑：###符号分割 + 保留名称内换行 ------------------------------
    #region 核心功能优化（纯符号方案 + 换行保留）
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
            // 每个名称后添加###（最后一个名称不加，避免多余标记）
            if (i < allObjects.Count - 1)
            {
                sb.Append(ObjectSeparator);
            }
        }
        nameList = sb.ToString();
        translationStatus = $"已提取 {allObjects.Count} 个名称（分隔符：{SpecialSeparator}）";
    }

    // 【优化2】分割名称：保留名称内换行，仅清理分隔符残留的前后空行/空格
    private string[] SplitNames(string text)
    {
        if (string.IsNullOrEmpty(text)) return new string[0];

        // 步骤1：统一换行符（处理Windows/macOS差异，保留名称内换行）
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 步骤2：按###分割（纯符号匹配，不破坏名称内换行）
        string[] specialSplitResult = Regex.Split(text, SpecialSeparator, RegexOptions.None)
            .Select(part =>
            {
                // 仅清理分割后残留的“前后空行/空格/制表符”，保留名称内部换行
                string cleanPart = part.Trim('\n', ' ', '\t');
                // 仅清理“多余空格”（不处理换行），避免名称内空格重复
                cleanPart = Regex.Replace(cleanPart, @" +", " ");
                return cleanPart;
            })
            .Where(part => !string.IsNullOrEmpty(part)) // 过滤空内容
            .ToArray();

        // 情况1：###分割结果匹配物体数 → 精准返回（含名称内换行）
        if (specialSplitResult.Length == allObjects.Count)
        {
            return specialSplitResult;
        }

        // 情况2：###失效（意外丢失），降级按空行分割（仍保留名称内换行）
        translationStatus = $"警告：{SpecialSeparator} 标记不完整，尝试按空行分割";
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

    // 【优化3】计数逻辑：依赖###分割结果，精准计数
    int CountNamesInList()
    {
        if (string.IsNullOrEmpty(nameList)) return 0;
        return SplitNames(nameList).Length;
    }
    #endregion

    // ------------------------------ 原有辅助功能（同步优化提示文本） ------------------------------
    #region 原有辅助功能
    string GetEnButtonText()
    {
        if (sourceLanguage == "zh-CHS" && targetLanguage == "en")
            return "中文 → 英语";
        else if (sourceLanguage == "en" && targetLanguage == "zh-CHS")
            return "英语 → 中文";
        return "英语 → 中文";
    }

    string GetJpButtonText()
    {
        if (sourceLanguage == "zh-CHS" && targetLanguage == "ja")
            return "中文 → 日语";
        else if (sourceLanguage == "ja" && targetLanguage == "zh-CHS")
            return "日语 → 中文";
        return "日语 → 中文";
    }

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

    void SetLanguagePair(string source, string target)
    {
        sourceLanguage = source;
        targetLanguage = target;
        translationStatus = $"已设置为 {GetLanguageName(source)} → {GetLanguageName(target)}";
    }

    string GetLanguageName(string langCode)
    {
        switch (langCode)
        {
            case "en": return "英语";
            case "ja": return "日语";
            case "zh-CHS": return "中文";
            default: return langCode;
        }
    }

    void ReverseTranslationDirection()
    {
        (sourceLanguage, targetLanguage) = (targetLanguage, sourceLanguage);
        translationStatus = $"已反转翻译方向: {GetLanguageName(sourceLanguage)} → {GetLanguageName(targetLanguage)}";
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
            isEnPairSelected = true;
            isJpPairSelected = false;
            SetLanguagePair("en", "zh-CHS");
        }
    }

    void StartTranslation()
    {
        if (string.IsNullOrEmpty(nameList))
        {
            EditorUtility.DisplayDialog("错误", "没有可翻译的内容", "确定");
            return;
        }
        translationStatus = "翻译中...";
        isTranslating = true;
        Repaint();

        // 翻译前先提取有效名称（排除###标记，保留内换行）
        string[] names = SplitNames(nameList);
        if (names.Length == 0)
        {
            translationStatus = "没有可翻译的内容（请检查分隔符）";
            isTranslating = false;
            return;
        }

        EditorApplication.delayCall += () =>
        {
            try
            {
                string apiUrl = $"https://suapi.net/api/text/translate?from={sourceLanguage}&to={targetLanguage}";
                foreach (var text in names)
                    apiUrl += $"&text[]={Uri.EscapeDataString(text.Trim())}";

                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/114.0.0.0 Safari/537.36");
                    string response = client.DownloadString(apiUrl);
                    var jsonResponse = JsonUtility.FromJson<TranslationResponse>(response);

                    if (jsonResponse.code == 200 && jsonResponse.data != null && jsonResponse.data.Length > 0)
                    {
                        StringBuilder translatedText = new StringBuilder();
                        for (int i = 0; i < jsonResponse.data.Length; i++)
                        {
                            var translationData = jsonResponse.data[i];
                            if (translationData.translations != null && translationData.translations.Length > 0)
                            {
                                // 保留翻译结果内的换行，仅添加分隔符
                                string translatedName = translationData.translations[0].text.Trim();
                                translatedText.Append(translatedName);
                                if (i < jsonResponse.data.Length - 1)
                                    translatedText.Append(ObjectSeparator);
                            }
                            else
                            {
                                translatedText.Append($"{translationData.detectedLanguage?.language}文本");
                                if (i < jsonResponse.data.Length - 1)
                                    translatedText.Append(ObjectSeparator);
                            }
                        }
                        nameList = translatedText.ToString();
                        translationStatus = $"翻译完成 ({names.Length}个名称)，已自动添加 {SpecialSeparator} 分隔符";
                    }
                    else
                    {
                        translationStatus = $"翻译失败: {jsonResponse.msg}";
                        Debug.LogError($"翻译失败: {jsonResponse.msg}\nAPI响应: {response}");
                    }
                }
            }
            catch (WebException webEx)
            {
                translationStatus = webEx.Response is HttpWebResponse httpResponse
                    ? $"网络错误: {httpResponse.StatusCode}"
                    : $"网络错误: {webEx.Message}";
                Debug.LogError($"网络错误: {webEx}");
            }
            catch (Exception ex)
            {
                translationStatus = $"翻译出错: {ex.Message}";
                Debug.LogError($"翻译错误: {ex}");
            }
            finally
            {
                isTranslating = false;
                Repaint();
            }
        };
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
                $"请确保名称间有 {SpecialSeparator} 分隔",
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

    // JSON解析类（无修改）
    [System.Serializable]
    private class DetectedLanguage { public string language; public float score; }
    [System.Serializable]
    private class Translation { public string text; public string to; public TranslationSentLen sentLen; }
    [System.Serializable]
    private class TranslationSentLen { public int[] srcSentLen; public int[] transSentLen; }
    [System.Serializable]
    private class TranslationData { public DetectedLanguage detectedLanguage; public Translation[] translations; }
    [System.Serializable]
    private class TranslationResponse { public int code; public string msg; public TranslationData[] data; public float exec_time; public string ip; }
    #endregion
}
#endif
