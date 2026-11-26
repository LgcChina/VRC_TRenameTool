// LGC TRenameTool 2.1.1
// VRChat 菜单文本提取、来源筛选、人工翻译回填与安全应用窗口。
// 更新日期：2026-11-26

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LGC.TRenameTool.Editor
{
    public sealed class TRenameTool : EditorWindow
    {
        internal const string ToolName = "LGC_TRenameTool_模型菜单翻译";
        internal const string Version = "2.1.1";
        internal const string UpdateDate = "2026-11-26";
        private const string ImportTextControlName = "LGC_TRenameTool_ImportText";

        private GameObject translationTarget;
        private UnityEngine.Object extraMenu;
        private bool includeInactive = true;
        private bool showOnlyChanged;
        private string searchText = string.Empty;
        private string exportText = string.Empty;
        private string importText = string.Empty;
        private string statusText = "请先选择待翻译物体或原生 VRC Expressions Menu。";
        private MessageType statusType = MessageType.Info;
        private Vector2 mainScroll;
        private Vector2 exportScroll;
        private Vector2 importScroll;
        private Vector2 previewScroll;
        private ScanResult scanResult = new ScanResult();
        private TranslationParseResult parseResult = new TranslationParseResult();

        [MenuItem("LGC/LGC_TRenameTool_模型菜单翻译")]
        public static void ShowWindow()
        {
            var window = GetWindow<TRenameTool>(ToolName + " " + Version);
            window.minSize = new Vector2(920, 640);
            window.Show();
        }

        [MenuItem("LGC/LGC_TRenameTool_模型菜单翻译", true)]
        private static bool ValidateShowWindow()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private void OnEnable()
        {
            minSize = new Vector2(920, 640);
            titleContent = new GUIContent(ToolName + " " + Version);
        }

        private void OnGUI()
        {
            DrawToolbar();
            mainScroll = EditorGUILayout.BeginScrollView(mainScroll, GUILayout.ExpandHeight(true));
            DrawSourceSection();
            DrawSummarySection();
            DrawTextSection();
            DrawValidationSection();
            DrawPreviewSection();
            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
            DrawFixedFooter();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(ToolName + "  v" + Version, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("更新：" + UpdateDate, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1. 扫描来源", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                translationTarget = (GameObject)EditorGUILayout.ObjectField(
                    new GUIContent("待翻译物体", "扫描此物体及其全部子级中的原生 VRC、Modular Avatar 和 VRCFury 菜单。"),
                    translationTarget,
                    typeof(GameObject),
                    true);

                extraMenu = EditorGUILayout.ObjectField(
                    new GUIContent("额外原生菜单", "可直接拖入一个 Assets 下的 VRCExpressionsMenu，与待翻译物体同时扫描。"),
                    extraMenu,
                    typeof(UnityEngine.Object),
                    false);

                EditorGUILayout.BeginHorizontal();
                includeInactive = EditorGUILayout.ToggleLeft("包含未激活物体", includeInactive, GUILayout.Width(150));
                if (GUILayout.Button("使用当前选中物体", GUILayout.Width(140)))
                {
                    translationTarget = Selection.activeGameObject;
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(translationTarget == null && extraMenu == null))
                {
                    if (GUILayout.Button("扫描待翻译按钮", GUILayout.Height(30), GUILayout.Width(170)))
                    {
                        Scan();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSummarySection()
        {
            if (scanResult.Entries.Count == 0 && scanResult.Warnings.Count == 0)
            {
                EditorGUILayout.HelpBox(statusText, statusType);
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("扫描结果", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var activeEntries = GetActiveEntries();
                EditorGUILayout.LabelField(
                    string.Format(
                        "共找到 {0} 条，当前选择 {1} 条：原生 VRC {2} / Modular Avatar {3} / VRCFury {4}",
                        scanResult.Entries.Count,
                        activeEntries.Count,
                        activeEntries.Count(e => e.SourceKind == TranslationSourceKind.VrcMenu),
                        activeEntries.Count(e => e.SourceKind == TranslationSourceKind.ModularAvatar),
                        activeEntries.Count(e => e.SourceKind == TranslationSourceKind.VrcFury)));

                if (scanResult.Warnings.Count > 0)
                {
                    EditorGUILayout.HelpBox(string.Join("\n", scanResult.Warnings.ToArray()), MessageType.Warning);
                }

                EditorGUILayout.HelpBox(statusText, statusType);
            }

            if (scanResult.Groups.Count > 0)
            {
                DrawSourceGroups();
            }
        }

        private void DrawSourceGroups()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("找到的菜单来源", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全选来源", GUILayout.Width(80)))
            {
                SetAllSourceGroups(true);
            }

            if (GUILayout.Button("全不选", GUILayout.Width(70)))
            {
                SetAllSourceGroups(false);
            }

            EditorGUILayout.EndHorizontal();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var group in scanResult.Groups)
                {
                    DrawSourceGroup(group);
                }
            }
        }

        private void DrawSourceGroup(TranslationSourceGroup group)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.BeginHorizontal();
                group.Expanded = GUILayout.Toggle(
                    group.Expanded,
                    GUIContent.none,
                    EditorStyles.foldout,
                    GUILayout.Width(14));
                var enabled = EditorGUILayout.Toggle(group.Enabled, GUILayout.Width(20));
                if (enabled != group.Enabled)
                {
                    SetSourceGroupEnabled(group, enabled);
                }

                GUILayout.Label(group.SourceSummary, EditorStyles.miniBoldLabel, GUILayout.Width(90));
                GUILayout.Label(group.Entries.Count + " 条", GUILayout.Width(45));
                GUILayout.Label(new GUIContent(group.DisplayPath, group.DisplayPath), EditorStyles.miniLabel);
                if (GUILayout.Button("定位", GUILayout.Width(50)))
                {
                    Locate(group.Owner);
                }

                EditorGUILayout.EndHorizontal();

                if (group.Expanded)
                {
                    foreach (var entry in group.Entries)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(34);
                        GUILayout.Label(entry.SourceKind.CompactName(), EditorStyles.miniLabel, GUILayout.Width(70));
                        GUILayout.Label(new GUIContent(entry.Original, entry.DisplayPath), EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
        }

        private void DrawTextSection()
        {
            if (GetActiveEntries().Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. 复制原文并粘贴译文", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "请完整保留 ID 编号。翻译网页即使更改空格、冒号、括号、空行或添加 Markdown 代码块，工具也会尝试恢复；编号全部丢失时，仅在非空行数量完全一致的情况下按顺序降级解析。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            DrawTextColumn("待翻译原文（只读）", ref exportText, ref exportScroll, true);
            GUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            DrawTextColumn("翻译后文本", ref importText, ref importScroll, false);
            if (EditorGUI.EndChangeCheck())
            {
                InvalidateParsedTranslations();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("复制待翻译文本", GUILayout.Height(28)))
            {
                EditorGUIUtility.systemCopyBuffer = exportText;
                SetStatus("已复制待翻译文本。", MessageType.Info);
            }

            if (GUILayout.Button("从剪贴板粘贴译文", GUILayout.Height(28)))
            {
                PasteTranslationsFromClipboard();
            }

            if (GUILayout.Button("清空译文", GUILayout.Height(28)))
            {
                ClearTranslations("已清空译文。");
            }

            if (GUILayout.Button("打开翻译网站 ▾", GUILayout.Height(28)))
            {
                ShowTranslationWebsiteMenu();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawTextColumn(string title, ref string value, ref Vector2 scroll, bool readOnly)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MinWidth(380)))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll, EditorStyles.helpBox, GUILayout.Height(190));
                if (readOnly)
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextArea(value, GUILayout.ExpandHeight(true));
                    }
                }
                else
                {
                    GUI.SetNextControlName(ImportTextControlName);
                    value = EditorGUILayout.TextArea(value, GUILayout.ExpandHeight(true));
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawValidationSection()
        {
            if (GetActiveEntries().Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("3. 校验译文", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("解析并更新预览", GUILayout.Width(160), GUILayout.Height(26)))
            {
                ParseTranslations();
            }

            EditorGUILayout.EndHorizontal();

            if (parseResult.HasAttempted)
            {
                EditorGUILayout.HelpBox(parseResult.Summary, parseResult.IsValid ? MessageType.Info : MessageType.Error);
                if (parseResult.Warnings.Count > 0)
                {
                    EditorGUILayout.HelpBox(string.Join("\n", parseResult.Warnings.ToArray()), MessageType.Warning);
                }
            }
        }

        private void DrawPreviewSection()
        {
            if (!parseResult.HasAttempted || GetActiveEntries().Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("4. 修改预览", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                showOnlyChanged = EditorGUILayout.ToggleLeft("只显示有变化", showOnlyChanged, GUILayout.Width(130));
                GUILayout.Label("搜索", GUILayout.Width(35));
                searchText = EditorGUILayout.TextField(searchText);
                if (GUILayout.Button("全选", GUILayout.Width(60)))
                {
                    foreach (var entry in GetActiveEntries())
                    {
                        entry.Selected = true;
                    }
                }

                if (GUILayout.Button("全不选", GUILayout.Width(60)))
                {
                    foreach (var entry in GetActiveEntries())
                    {
                        entry.Selected = false;
                    }
                }
            }

            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, EditorStyles.helpBox, GUILayout.Height(250));
            foreach (var entry in FilteredEntries())
            {
                DrawPreviewEntry(entry);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(
                "应用支持 Unity Undo，但不会自动保存场景、Prefab 或资源。MA 菜单 label 为空时，译文会写入 label，不会重命名 GameObject。",
                MessageType.Warning);
        }

        private IEnumerable<TranslationEntry> FilteredEntries()
        {
            var query = GetActiveEntries().AsEnumerable();
            if (showOnlyChanged)
            {
                query = query.Where(entry => entry.HasChange);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var needle = searchText.Trim();
                query = query.Where(entry =>
                    entry.SourceKind.DisplayName().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.SourceKind.CompactName().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.Original.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (entry.Translation ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.DisplayPath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.SourceGroup.DisplayPath.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return query;
        }

        private static void DrawPreviewEntry(TranslationEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));
                GUILayout.Label(entry.SourceKind.CompactName(), EditorStyles.miniBoldLabel, GUILayout.Width(72));
                GUILayout.Label(new GUIContent("（原名）" + entry.Original, entry.Original), GUILayout.Width(180));
                GUILayout.Label("→", GUILayout.Width(18));
                GUILayout.Label(
                    new GUIContent("（译名）" + (entry.Translation ?? string.Empty), entry.Translation ?? string.Empty),
                    entry.HasChange ? EditorStyles.boldLabel : EditorStyles.label,
                    GUILayout.Width(180));
                GUILayout.Space(12);
                GUILayout.Label(new GUIContent(entry.DisplayPath, entry.DisplayPath), EditorStyles.miniLabel, GUILayout.MinWidth(150));
                if (GUILayout.Button("定位", GUILayout.Width(50)))
                {
                    Locate(entry.SourceGroup.Owner);
                }
            }
        }

        private void DrawFixedFooter()
        {
            var activeEntries = GetActiveEntries();
            var changedSelected = activeEntries.Count(entry => entry.Selected && entry.HasChange);
            var validation = !parseResult.HasAttempted ? "尚未校验" : parseResult.IsValid ? "校验通过" : "校验失败";
            var warningCount = scanResult.Warnings.Count + parseResult.Warnings.Count;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox, GUILayout.Height(46)))
            {
                GUILayout.Label(
                    string.Format(
                        "{0}  |  已选择 {1} 项修改  |  {2} 个错误  |  {3} 个警告",
                        validation,
                        changedSelected,
                        parseResult.ErrorCount,
                        warningCount),
                    parseResult.IsValid ? EditorStyles.boldLabel : EditorStyles.label);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(translationTarget == null && extraMenu == null))
                {
                    if (GUILayout.Button("重新扫描", GUILayout.Width(100), GUILayout.Height(30)))
                    {
                        Scan();
                    }
                }

                using (new EditorGUI.DisabledScope(!parseResult.IsValid || changedSelected == 0))
                {
                    var previousColor = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.55f, 0.9f, 0.65f);
                    if (GUILayout.Button("应用所选修改（" + changedSelected + "）", GUILayout.Width(190), GUILayout.Height(30)))
                    {
                        ApplyTranslations();
                    }

                    GUI.backgroundColor = previousColor;
                }
            }
        }

        private void Scan()
        {
            try
            {
                scanResult = MenuTranslationScanner.Scan(translationTarget, extraMenu, includeInactive);
                importText = string.Empty;
                parseResult = new TranslationParseResult();
                RebuildExportText();
                if (scanResult.Entries.Count == 0)
                {
                    SetStatus("扫描完成，但没有找到可翻译的菜单文字。请确认选择的是待翻译物体或 Assets 下的 VRCExpressionsMenu。", MessageType.Warning);
                }
                else
                {
                    SetStatus(
                        "扫描完成：在 " + scanResult.Groups.Count + " 个物体或资源中提取了 " + scanResult.Entries.Count + " 条菜单文字。",
                        MessageType.Info);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                scanResult = new ScanResult();
                exportText = string.Empty;
                importText = string.Empty;
                parseResult = new TranslationParseResult();
                SetStatus("扫描失败：" + exception.Message, MessageType.Error);
            }
        }

        private void ParseTranslations()
        {
            var activeEntries = GetActiveEntries();
            parseResult = TranslationTextCodec.Parse(importText, activeEntries);
            SetStatus(parseResult.Summary, parseResult.IsValid ? MessageType.Info : MessageType.Error);
        }

        private void PasteTranslationsFromClipboard()
        {
            importText = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
            importScroll = Vector2.zero;
            GUI.FocusControl(null);
            Repaint();
            ParseTranslations();
        }

        private void ApplyTranslations()
        {
            if (!parseResult.IsValid)
            {
                SetStatus("译文校验未通过，未进行任何修改。", MessageType.Error);
                return;
            }

            var result = TranslationApplier.Apply(GetActiveEntries());
            SetStatus(result.Message, result.Success ? MessageType.Info : MessageType.Error);
            if (result.Success)
            {
                Scan();
                statusText = result.Message + " 已重新扫描，请在 Unity 中检查结果；工具未自动保存。";
            }
        }

        private void SetSourceGroupEnabled(TranslationSourceGroup group, bool enabled)
        {
            if (!ConfirmSourceSelectionReset())
            {
                return;
            }

            group.Enabled = enabled;
            ResetAfterSourceSelectionChanged();
        }

        private void SetAllSourceGroups(bool enabled)
        {
            if (scanResult.Groups.All(group => group.Enabled == enabled) || !ConfirmSourceSelectionReset())
            {
                return;
            }

            foreach (var group in scanResult.Groups)
            {
                group.Enabled = enabled;
            }

            ResetAfterSourceSelectionChanged();
        }

        private bool ConfirmSourceSelectionReset()
        {
            if (string.IsNullOrWhiteSpace(importText) && !parseResult.HasAttempted)
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "切换菜单来源",
                "切换来源会重建 ID 编号，并清空当前译文与修改预览，避免译文对应到错误按钮。是否继续？",
                "切换并清空",
                "取消");
        }

        private void ResetAfterSourceSelectionChanged()
        {
            foreach (var entry in scanResult.Entries)
            {
                entry.Id = string.Empty;
                entry.Translation = string.Empty;
                entry.Selected = true;
            }

            importText = string.Empty;
            parseResult = new TranslationParseResult();
            RebuildExportText();
            SetStatus("已按所选来源重建待翻译文本。", MessageType.Info);
        }

        private void ClearTranslations(string message)
        {
            GUI.FocusControl(null);
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            importText = string.Empty;
            importScroll = Vector2.zero;
            parseResult = new TranslationParseResult();
            foreach (var entry in scanResult.Entries)
            {
                entry.Translation = string.Empty;
            }

            SetStatus(message, MessageType.Info);
            EditorApplication.delayCall += ClearTranslationsAfterGuiEvent;
        }

        private void ClearTranslationsAfterGuiEvent()
        {
            EditorApplication.delayCall -= ClearTranslationsAfterGuiEvent;
            GUI.FocusControl(null);
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
            importText = string.Empty;
            importScroll = Vector2.zero;
            parseResult = new TranslationParseResult();
            foreach (var entry in scanResult.Entries)
            {
                entry.Translation = string.Empty;
            }

            Repaint();
        }

        private void InvalidateParsedTranslations()
        {
            parseResult = new TranslationParseResult();
            foreach (var entry in scanResult.Entries)
            {
                entry.Translation = string.Empty;
            }

            statusText = "译文内容已更改，请重新解析并检查预览。";
            statusType = MessageType.Info;
        }

        private void RebuildExportText()
        {
            foreach (var entry in scanResult.Entries)
            {
                entry.Id = string.Empty;
            }

            exportText = TranslationTextCodec.Export(GetActiveEntries());
        }

        private List<TranslationEntry> GetActiveEntries()
        {
            return scanResult.Entries
                .Where(entry => entry.SourceGroup != null && entry.SourceGroup.Enabled)
                .ToList();
        }

        private static void Locate(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            Selection.activeObject = target;
            EditorGUIUtility.PingObject(target);
        }

        private void ShowTranslationWebsiteMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Google 翻译"), false, () => OpenUrl("https://translate.google.com/"));
            menu.AddItem(new GUIContent("Bing 翻译"), false, () => OpenUrl("https://www.bing.com/translator"));
            menu.AddItem(new GUIContent("百度翻译"), false, () => OpenUrl("https://fanyi.baidu.com/"));
            menu.AddItem(new GUIContent("有道翻译"), false, () => OpenUrl("https://fanyi.youdao.com/"));
            menu.ShowAsContext();
        }

        private static void OpenUrl(string url)
        {
            Application.OpenURL(url);
        }

        private void SetStatus(string message, MessageType type)
        {
            statusText = message;
            statusType = type;
            Repaint();
        }
    }
}
#endif
