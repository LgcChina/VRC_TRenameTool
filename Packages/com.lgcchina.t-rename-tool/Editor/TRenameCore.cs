// LGC TRenameTool 2.1.1
// 菜单扫描、稳定编号文本编解码与安全写回核心。
// 更新日期：2026-11-26

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LGC.TRenameTool.Editor
{
    internal enum TranslationSourceKind
    {
        VrcMenu,
        ModularAvatar,
        VrcFury
    }

    internal static class TranslationSourceKindExtensions
    {
        public static string DisplayName(this TranslationSourceKind kind)
        {
            switch (kind)
            {
                case TranslationSourceKind.VrcMenu:
                    return "原生 VRC";
                case TranslationSourceKind.ModularAvatar:
                    return "Modular Avatar";
                case TranslationSourceKind.VrcFury:
                    return "VRCFury";
                default:
                    return kind.ToString();
            }
        }

        public static string CompactName(this TranslationSourceKind kind)
        {
            switch (kind)
            {
                case TranslationSourceKind.VrcMenu:
                    return "VRC";
                case TranslationSourceKind.ModularAvatar:
                    return "MA";
                case TranslationSourceKind.VrcFury:
                    return "VRCFury";
                default:
                    return kind.ToString();
            }
        }
    }

    internal sealed class TranslationSourceGroup
    {
        public Object Owner;
        public string DisplayPath;
        public bool Enabled = true;
        public bool Expanded;
        public readonly List<TranslationEntry> Entries = new List<TranslationEntry>();

        public string SourceSummary
        {
            get
            {
                return string.Join(
                    " + ",
                    Entries.Select(entry => entry.SourceKind.CompactName()).Distinct().ToArray());
            }
        }
    }

    internal sealed class TranslationEntry
    {
        public string Id;
        public TranslationSourceKind SourceKind;
        public string Original;
        public string Translation = string.Empty;
        public Object Target;
        public string PropertyPath;
        public string DisplayPath;
        public string SnapshotRawValue;
        public int PathSegmentIndex = -1;
        public bool Selected = true;
        public TranslationSourceGroup SourceGroup;

        public bool IsPathSegment
        {
            get { return PathSegmentIndex >= 0; }
        }

        public bool HasChange
        {
            get
            {
                return !string.IsNullOrEmpty(Translation) &&
                       !string.Equals(Original, Translation, StringComparison.Ordinal);
            }
        }
    }

    internal sealed class ScanResult
    {
        public readonly List<TranslationEntry> Entries = new List<TranslationEntry>();
        public readonly List<TranslationSourceGroup> Groups = new List<TranslationSourceGroup>();
        public readonly List<string> Warnings = new List<string>();

        public int CountOf(TranslationSourceKind kind)
        {
            return Entries.Count(e => e.SourceKind == kind);
        }
    }

    internal sealed class TranslationParseResult
    {
        public bool HasAttempted;
        public bool IsValid;
        public int ErrorCount;
        public string Summary = string.Empty;
        public readonly List<string> Warnings = new List<string>();
    }

    internal sealed class ApplyResult
    {
        public bool Success;
        public string Message;
    }

    internal static class TranslationTextCodec
    {
        private static readonly Regex MarkerRegex = new Regex(
            @"(?ix)(?<![A-Z0-9])(?:[\[\(【]\s*)?ID\s*0*(?<id>\d{1,6})\s*(?:[\]\)】])?\s*(?:(?:[:：\-–—]\s*)|(?:\s+))",
            RegexOptions.Compiled);

        private static readonly Regex CodeFenceRegex = new Regex(@"(?m)^\s*```[^\r\n]*$", RegexOptions.Compiled);
        private static readonly Regex LeadingListRegex = new Regex(@"^\s*(?:[-*•·]+|\d+[.)、])\s*", RegexOptions.Compiled);
        private static readonly Regex TrailingSeparatorRegex = new Regex(@"(?:\r?\n)?\s*(?:[-*•·]+)?\s*$", RegexOptions.Compiled);

        public static string Export(IList<TranslationEntry> entries)
        {
            var builder = new StringBuilder();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                entry.Id = FormatId(index + 1);
                builder.Append(entry.Id);
                builder.Append("  ");
                builder.Append(Escape(entry.Original));
                builder.Append('\n');
            }

            return builder.ToString().TrimEnd();
        }

        public static TranslationParseResult Parse(string text, IList<TranslationEntry> entries)
        {
            var result = new TranslationParseResult { HasAttempted = true };
            foreach (var entry in entries)
            {
                entry.Translation = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                result.ErrorCount = 1;
                result.Summary = "译文为空。";
                return result;
            }

            var normalized = Normalize(text);
            var matches = MarkerRegex.Matches(normalized).Cast<Match>().ToList();
            var values = new Dictionary<int, string>();
            var duplicates = new HashSet<int>();

            if (matches.Count > 0)
            {
                for (var index = 0; index < matches.Count; index++)
                {
                    var match = matches[index];
                    int id;
                    if (!int.TryParse(match.Groups["id"].Value, out id))
                    {
                        continue;
                    }

                    var contentStart = match.Index + match.Length;
                    var contentEnd = index + 1 < matches.Count ? matches[index + 1].Index : normalized.Length;
                    var content = CleanValue(normalized.Substring(contentStart, contentEnd - contentStart));
                    if (values.ContainsKey(id))
                    {
                        duplicates.Add(id);
                    }
                    else
                    {
                        values[id] = content;
                    }
                }
            }
            else
            {
                var lines = normalized
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(CleanValue)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();

                if (lines.Count != entries.Count)
                {
                    result.ErrorCount = 1;
                    result.Summary = string.Format(
                        "没有识别到 ID 编号，且非空行数量为 {0}，与待翻译条目 {1} 不一致。",
                        lines.Count,
                        entries.Count);
                    return result;
                }

                result.Warnings.Add("所有 ID 编号均已丢失，本次按非空行顺序降级匹配；应用前请仔细检查预览。");
                for (var index = 0; index < lines.Count; index++)
                {
                    values[index + 1] = lines[index];
                }
            }

            var errors = new List<string>();
            if (duplicates.Count > 0)
            {
                errors.Add("编号重复：" + string.Join(", ", duplicates.OrderBy(id => id).Select(FormatId).ToArray()));
            }

            var expectedIds = new HashSet<int>(Enumerable.Range(1, entries.Count));
            var unknownIds = values.Keys.Where(id => !expectedIds.Contains(id)).OrderBy(id => id).ToList();
            if (unknownIds.Count > 0)
            {
                errors.Add("未知编号：" + string.Join(", ", unknownIds.Select(FormatId).ToArray()));
            }

            var missingIds = expectedIds.Where(id => !values.ContainsKey(id)).OrderBy(id => id).ToList();
            if (missingIds.Count > 0)
            {
                errors.Add("缺少编号：" + string.Join(", ", missingIds.Select(FormatId).ToArray()));
            }

            foreach (var entry in entries)
            {
                int id;
                if (!TryParseEntryId(entry.Id, out id) || !values.ContainsKey(id))
                {
                    continue;
                }

                var value = Unescape(values[id]);
                entry.Translation = value;
                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(entry.Id + " 的译文为空。");
                }
                else if (entry.IsPathSegment && (value.Contains("/") || value.Contains("\\")))
                {
                    errors.Add(entry.Id + " 是菜单路径片段，译文不能包含 / 或 \\。请只填写当前层级名称。");
                }
            }

            if (errors.Count > 0)
            {
                var distinctErrors = errors.Distinct().ToArray();
                result.ErrorCount = distinctErrors.Length;
                result.Summary = "译文校验失败：\n" + string.Join("\n", distinctErrors);
                return result;
            }

            result.IsValid = true;
            var changed = entries.Count(entry => entry.HasChange);
            result.Summary = string.Format("译文校验通过：{0}/{1} 条将发生变化。", changed, entries.Count);
            return result;
        }

        private static string Normalize(string text)
        {
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            normalized = normalized.Replace('\u00A0', ' ')
                .Replace("\u200B", string.Empty)
                .Replace("\u200C", string.Empty)
                .Replace("\u200D", string.Empty)
                .Replace("\uFEFF", string.Empty);
            normalized = CodeFenceRegex.Replace(normalized, string.Empty);
            return WebUtility.HtmlDecode(normalized);
        }

        private static string CleanValue(string value)
        {
            value = value.Trim();
            value = LeadingListRegex.Replace(value, string.Empty);
            value = TrailingSeparatorRegex.Replace(value, string.Empty).Trim();

            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[value.Length - 1];
                if ((first == '"' && last == '"') ||
                    (first == '\'' && last == '\'') ||
                    (first == '“' && last == '”') ||
                    (first == '「' && last == '」'))
                {
                    value = value.Substring(1, value.Length - 2).Trim();
                }
            }

            return value;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
        }

        private static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '\\' && index + 1 < value.Length)
                {
                    if (value[index + 1] == 'n')
                    {
                        builder.Append('\n');
                        index++;
                        continue;
                    }

                    if (value[index + 1] == '\\')
                    {
                        builder.Append('\\');
                        index++;
                        continue;
                    }
                }

                builder.Append(value[index]);
            }

            return builder.ToString().Trim();
        }

        private static bool TryParseEntryId(string idText, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(idText) || idText.Length <= 2 ||
                !idText.StartsWith("ID", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(idText.Substring(2), out id);
        }

        private static string FormatId(int id)
        {
            return "ID" + id.ToString("D4");
        }
    }

    internal static class MenuTranslationScanner
    {
        private const string VrcMenuType = "VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu";
        private const string AvatarDescriptorType = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";
        private const string MaMenuItemType = "nadena.dev.modular_avatar.core.ModularAvatarMenuItem";
        private const string MaMenuInstallerType = "nadena.dev.modular_avatar.core.ModularAvatarMenuInstaller";
        private const string VrcFuryType = "VF.Model.VRCFury";

        private sealed class Context
        {
            public readonly ScanResult Result = new ScanResult();
            public readonly HashSet<int> VisitedMenus = new HashSet<int>();
            public readonly HashSet<string> EntryKeys = new HashSet<string>();
            public readonly Dictionary<int, TranslationSourceGroup> Groups = new Dictionary<int, TranslationSourceGroup>();
            public GameObject Root;
        }

        public static ScanResult Scan(GameObject translationTarget, Object extraMenu, bool includeInactive)
        {
            var context = new Context { Root = translationTarget };

            if (extraMenu != null)
            {
                if (IsVrcMenu(extraMenu))
                {
                    ScanVrcMenu(context, extraMenu, "额外菜单");
                }
                else
                {
                    context.Result.Warnings.Add("“额外原生菜单”不是 VRCExpressionsMenu，已忽略：" + extraMenu.name);
                }
            }

            if (translationTarget != null)
            {
                var behaviours = translationTarget.GetComponentsInChildren<MonoBehaviour>(includeInactive);
                foreach (var behaviour in behaviours)
                {
                    if (behaviour == null)
                    {
                        continue;
                    }

                    var fullName = behaviour.GetType().FullName;
                    if (fullName == AvatarDescriptorType)
                    {
                        ScanAvatarDescriptor(context, behaviour);
                    }
                    else if (fullName == MaMenuItemType)
                    {
                        ScanMaMenuItem(context, behaviour);
                    }
                    else if (fullName == MaMenuInstallerType)
                    {
                        ScanMaInstaller(context, behaviour);
                    }
                    else if (fullName == VrcFuryType)
                    {
                        ScanVrcFury(context, behaviour);
                    }
                }
            }

            return context.Result;
        }

        private static void ScanAvatarDescriptor(Context context, MonoBehaviour descriptor)
        {
            var serialized = new SerializedObject(descriptor);
            var menuProperty = serialized.FindProperty("expressionsMenu");
            if (menuProperty != null && menuProperty.propertyType == SerializedPropertyType.ObjectReference)
            {
                ScanVrcMenu(context, menuProperty.objectReferenceValue, "Avatar Descriptor");
            }
        }

        private static void ScanMaMenuItem(Context context, MonoBehaviour component)
        {
            var serialized = new SerializedObject(component);
            var label = serialized.FindProperty("label");
            if (label != null && label.propertyType == SerializedPropertyType.String)
            {
                var displayName = string.IsNullOrEmpty(label.stringValue) ? component.gameObject.name : label.stringValue;
                AddStringEntry(
                    context,
                    component,
                    label.propertyPath,
                    displayName,
                    label.stringValue,
                    TranslationSourceKind.ModularAvatar,
                    GetHierarchyPath(context.Root, component.gameObject) + " / 菜单名");
            }

            var control = serialized.FindProperty("Control");
            if (control == null)
            {
                return;
            }

            var subMenu = control.FindPropertyRelative("subMenu");
            if (subMenu != null && subMenu.propertyType == SerializedPropertyType.ObjectReference)
            {
                ScanVrcMenu(context, subMenu.objectReferenceValue, GetHierarchyPath(context.Root, component.gameObject));
            }

            ScanLabelArray(
                context,
                component,
                control.FindPropertyRelative("labels"),
                TranslationSourceKind.ModularAvatar,
                GetHierarchyPath(context.Root, component.gameObject));
        }

        private static void ScanMaInstaller(Context context, MonoBehaviour component)
        {
            var serialized = new SerializedObject(component);
            ScanReferencedMenuProperty(context, serialized.FindProperty("menuToAppend"), GetHierarchyPath(context.Root, component.gameObject) + " / MA Menu Installer");
            ScanReferencedMenuProperty(context, serialized.FindProperty("installTargetMenu"), GetHierarchyPath(context.Root, component.gameObject) + " / MA 安装目标");
        }

        private static void ScanVrcFury(Context context, MonoBehaviour component)
        {
            var serialized = new SerializedObject(component);
            var content = serialized.FindProperty("content");
            if (content != null && content.propertyType == SerializedPropertyType.ManagedReference && content.managedReferenceValue != null)
            {
                ScanVrcFuryFeature(context, component, content, GetHierarchyPath(context.Root, component.gameObject));
            }

            var legacyConfig = serialized.FindProperty("config");
            var legacyFeatures = legacyConfig == null ? null : legacyConfig.FindPropertyRelative("features");
            if (legacyFeatures != null && legacyFeatures.isArray)
            {
                for (var index = 0; index < legacyFeatures.arraySize; index++)
                {
                    var feature = legacyFeatures.GetArrayElementAtIndex(index);
                    if (feature.propertyType == SerializedPropertyType.ManagedReference && feature.managedReferenceValue != null)
                    {
                        ScanVrcFuryFeature(context, component, feature, GetHierarchyPath(context.Root, component.gameObject));
                    }
                }
            }
        }

        private static void ScanVrcFuryFeature(Context context, MonoBehaviour component, SerializedProperty feature, string hierarchyPath)
        {
            var typeName = GetManagedReferenceClassName(feature.managedReferenceFullTypename);
            switch (typeName)
            {
                case "Toggle":
                case "Puppet":
                    ScanPathProperty(context, component, feature.FindPropertyRelative("name"), TranslationSourceKind.VrcFury, hierarchyPath + " / " + typeName);
                    break;
                case "MoveMenuItem":
                    ScanPathProperty(context, component, feature.FindPropertyRelative("fromPath"), TranslationSourceKind.VrcFury, hierarchyPath + " / 移动来源");
                    ScanPathProperty(context, component, feature.FindPropertyRelative("toPath"), TranslationSourceKind.VrcFury, hierarchyPath + " / 移动目标");
                    break;
                case "ReorderMenuItem":
                    ScanPathProperty(context, component, feature.FindPropertyRelative("path"), TranslationSourceKind.VrcFury, hierarchyPath + " / 重排路径");
                    break;
                case "SpsOptions":
                case "WorldConstraint":
                    ScanPathProperty(context, component, feature.FindPropertyRelative("menuPath"), TranslationSourceKind.VrcFury, hierarchyPath + " / " + typeName);
                    break;
                case "FullController":
                    ScanFullController(context, component, feature, hierarchyPath);
                    break;
            }
        }

        private static void ScanFullController(Context context, MonoBehaviour component, SerializedProperty feature, string hierarchyPath)
        {
            var menus = feature.FindPropertyRelative("menus");
            if (menus != null && menus.isArray)
            {
                for (var index = 0; index < menus.arraySize; index++)
                {
                    var item = menus.GetArrayElementAtIndex(index);
                    ScanPathProperty(context, component, item.FindPropertyRelative("prefix"), TranslationSourceKind.VrcFury, hierarchyPath + " / Full Controller 前缀");

                    var menu = item.FindPropertyRelative("menu");
                    var menuReference = menu == null ? null : menu.FindPropertyRelative("objRef");
                    ScanReferencedMenuProperty(context, menuReference, hierarchyPath + " / Full Controller");
                }
            }

            var legacyMenu = feature.FindPropertyRelative("menu");
            var legacyMenuReference = legacyMenu == null ? null : legacyMenu.FindPropertyRelative("objRef");
            ScanReferencedMenuProperty(context, legacyMenuReference, hierarchyPath + " / Full Controller 旧菜单");
            ScanPathProperty(context, component, feature.FindPropertyRelative("submenu"), TranslationSourceKind.VrcFury, hierarchyPath + " / Full Controller 旧前缀");
        }

        private static void ScanVrcMenu(Context context, Object menu, string sourcePath)
        {
            if (menu == null || !IsVrcMenu(menu))
            {
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(menu);
            if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                var warning = "已跳过 Packages 内不可修改的原生菜单资源：" + assetPath;
                if (!context.Result.Warnings.Contains(warning))
                {
                    context.Result.Warnings.Add(warning);
                }

                return;
            }

            if (!context.VisitedMenus.Add(menu.GetInstanceID()))
            {
                return;
            }

            var serialized = new SerializedObject(menu);
            var controls = serialized.FindProperty("controls");
            if (controls == null || !controls.isArray)
            {
                context.Result.Warnings.Add("无法读取原生菜单 controls：" + menu.name);
                return;
            }

            var menuPath = string.IsNullOrEmpty(assetPath) ? sourcePath + " / " + menu.name : assetPath;
            for (var index = 0; index < controls.arraySize; index++)
            {
                var control = controls.GetArrayElementAtIndex(index);
                var name = control.FindPropertyRelative("name");
                if (name != null && name.propertyType == SerializedPropertyType.String && !string.IsNullOrWhiteSpace(name.stringValue))
                {
                    AddStringEntry(
                        context,
                        menu,
                        name.propertyPath,
                        name.stringValue,
                        name.stringValue,
                        TranslationSourceKind.VrcMenu,
                        menuPath + " / Control " + (index + 1));
                }

                ScanLabelArray(context, menu, control.FindPropertyRelative("labels"), TranslationSourceKind.VrcMenu, menuPath + " / Control " + (index + 1));
                ScanReferencedMenuProperty(context, control.FindPropertyRelative("subMenu"), menuPath + " / " + (name == null ? "子菜单" : name.stringValue));
            }
        }

        private static void ScanLabelArray(Context context, Object target, SerializedProperty labels, TranslationSourceKind kind, string displayPath)
        {
            if (labels == null || !labels.isArray)
            {
                return;
            }

            for (var index = 0; index < labels.arraySize; index++)
            {
                var label = labels.GetArrayElementAtIndex(index).FindPropertyRelative("name");
                if (label == null || label.propertyType != SerializedPropertyType.String || string.IsNullOrWhiteSpace(label.stringValue))
                {
                    continue;
                }

                AddStringEntry(
                    context,
                    target,
                    label.propertyPath,
                    label.stringValue,
                    label.stringValue,
                    kind,
                    displayPath + " / Puppet Label " + (index + 1));
            }
        }

        private static void ScanPathProperty(Context context, Object target, SerializedProperty property, TranslationSourceKind kind, string displayPath)
        {
            if (property == null || property.propertyType != SerializedPropertyType.String || string.IsNullOrWhiteSpace(property.stringValue))
            {
                return;
            }

            var raw = property.stringValue;
            var segments = raw.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index].Trim();
                if (string.IsNullOrEmpty(segment))
                {
                    continue;
                }

                AddEntry(
                    context,
                    target,
                    property.propertyPath,
                    segment,
                    raw,
                    kind,
                    displayPath + " / 路径层级 " + (index + 1),
                    index);
            }
        }

        private static void ScanReferencedMenuProperty(Context context, SerializedProperty property, string sourcePath)
        {
            if (property != null && property.propertyType == SerializedPropertyType.ObjectReference)
            {
                ScanVrcMenu(context, property.objectReferenceValue, sourcePath);
            }
        }

        private static void AddStringEntry(Context context, Object target, string propertyPath, string displayValue, string rawValue, TranslationSourceKind kind, string displayPath)
        {
            if (!string.IsNullOrWhiteSpace(displayValue))
            {
                AddEntry(context, target, propertyPath, displayValue, rawValue, kind, displayPath, -1);
            }
        }

        private static void AddEntry(Context context, Object target, string propertyPath, string original, string rawValue, TranslationSourceKind kind, string displayPath, int pathSegmentIndex)
        {
            if (target == null || string.IsNullOrEmpty(propertyPath))
            {
                return;
            }

            var key = target.GetInstanceID() + "|" + propertyPath + "|" + pathSegmentIndex;
            if (!context.EntryKeys.Add(key))
            {
                return;
            }

            var sourceGroup = GetOrCreateSourceGroup(context, target);
            var entry = new TranslationEntry
            {
                SourceKind = kind,
                Original = original,
                Target = target,
                PropertyPath = propertyPath,
                DisplayPath = displayPath,
                SnapshotRawValue = rawValue ?? string.Empty,
                PathSegmentIndex = pathSegmentIndex,
                SourceGroup = sourceGroup
            };
            context.Result.Entries.Add(entry);
            sourceGroup.Entries.Add(entry);
        }

        private static TranslationSourceGroup GetOrCreateSourceGroup(Context context, Object target)
        {
            Object owner = target;
            var component = target as Component;
            if (component != null)
            {
                owner = component.gameObject;
            }

            var key = owner.GetInstanceID();
            TranslationSourceGroup group;
            if (context.Groups.TryGetValue(key, out group))
            {
                return group;
            }

            string displayPath;
            var gameObject = owner as GameObject;
            if (gameObject != null)
            {
                displayPath = GetHierarchyPath(context.Root, gameObject);
            }
            else
            {
                var assetPath = AssetDatabase.GetAssetPath(owner);
                displayPath = string.IsNullOrEmpty(assetPath) ? owner.name : assetPath;
            }

            group = new TranslationSourceGroup
            {
                Owner = owner,
                DisplayPath = displayPath
            };
            context.Groups.Add(key, group);
            context.Result.Groups.Add(group);
            return group;
        }

        private static bool IsVrcMenu(Object value)
        {
            return value != null && value.GetType().FullName == VrcMenuType;
        }

        private static string GetManagedReferenceClassName(string fullTypename)
        {
            if (string.IsNullOrEmpty(fullTypename))
            {
                return string.Empty;
            }

            var split = fullTypename.LastIndexOf('.');
            return split >= 0 ? fullTypename.Substring(split + 1) : fullTypename;
        }

        private static string GetHierarchyPath(GameObject root, GameObject target)
        {
            if (target == null)
            {
                return "<Missing>";
            }

            if (root == null)
            {
                return target.name;
            }

            var relative = AnimationUtility.CalculateTransformPath(target.transform, root.transform);
            return string.IsNullOrEmpty(relative) ? root.name : root.name + "/" + relative;
        }
    }

    internal static class TranslationApplier
    {
        private sealed class PropertyChange
        {
            public Object Target;
            public string PropertyPath;
            public string SnapshotRawValue;
            public readonly List<TranslationEntry> Entries = new List<TranslationEntry>();
        }

        public static ApplyResult Apply(IList<TranslationEntry> entries)
        {
            var selected = entries.Where(entry => entry.Selected && entry.HasChange).ToList();
            if (selected.Count == 0)
            {
                return Failure("没有选中任何有变化的条目。");
            }

            if (selected.Any(entry => entry.Target == null))
            {
                return Failure("目标对象已被删除。请重新扫描后再应用。");
            }

            var changes = new List<PropertyChange>();
            foreach (var group in selected.GroupBy(entry => entry.Target.GetInstanceID() + "|" + entry.PropertyPath))
            {
                var first = group.First();
                var change = new PropertyChange
                {
                    Target = first.Target,
                    PropertyPath = first.PropertyPath,
                    SnapshotRawValue = first.SnapshotRawValue
                };
                change.Entries.AddRange(group);
                changes.Add(change);
            }

            var errors = ValidateChanges(changes);
            if (errors.Count > 0)
            {
                return Failure("应用已取消，未修改任何内容：\n" + string.Join("\n", errors.ToArray()));
            }

            var targets = changes.Select(change => change.Target).Distinct().ToArray();
            Undo.RecordObjects(targets, "翻译 VRChat 菜单");

            var changedEntries = 0;
            foreach (var targetGroup in changes.GroupBy(change => change.Target))
            {
                var target = targetGroup.Key;
                var serialized = new SerializedObject(target);
                serialized.Update();
                foreach (var change in targetGroup)
                {
                    var property = serialized.FindProperty(change.PropertyPath);
                    property.stringValue = BuildNewValue(change);
                    changedEntries += change.Entries.Count;
                }

                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);

                var component = target as Component;
                if (component != null)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(component))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }

                    if (component.gameObject.scene.IsValid())
                    {
                        EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                    }
                }
            }

            Undo.FlushUndoRecordObjects();
            return new ApplyResult
            {
                Success = true,
                Message = "已应用 " + changedEntries + " 条菜单文字修改。"
            };
        }

        private static List<string> ValidateChanges(IEnumerable<PropertyChange> changes)
        {
            var errors = new List<string>();
            foreach (var change in changes)
            {
                if (change.Target == null)
                {
                    errors.Add("目标对象已被删除。请重新扫描。");
                    continue;
                }

                var assetPath = AssetDatabase.GetAssetPath(change.Target);
                if (EditorUtility.IsPersistent(change.Target) &&
                    !string.IsNullOrEmpty(assetPath) &&
                    assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("工具不支持修改 Packages 内的资源：" + assetPath);
                    continue;
                }

                var serialized = new SerializedObject(change.Target);
                var property = serialized.FindProperty(change.PropertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.String)
                {
                    errors.Add("属性已不存在：" + change.Target.name + " / " + change.PropertyPath);
                    continue;
                }

                if (!string.Equals(property.stringValue, change.SnapshotRawValue, StringComparison.Ordinal))
                {
                    errors.Add("扫描后内容已变化，请重新扫描：" + change.Target.name + " / " + change.PropertyPath);
                    continue;
                }

                if (change.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.Translation)))
                {
                    errors.Add("存在空译文：" + change.Target.name + " / " + change.PropertyPath);
                }
            }

            return errors.Distinct().ToList();
        }

        private static string BuildNewValue(PropertyChange change)
        {
            if (!change.Entries.Any(entry => entry.IsPathSegment))
            {
                return change.Entries[0].Translation.Trim();
            }

            var segments = change.SnapshotRawValue.Split('/');
            foreach (var entry in change.Entries)
            {
                if (entry.PathSegmentIndex >= 0 && entry.PathSegmentIndex < segments.Length)
                {
                    segments[entry.PathSegmentIndex] = entry.Translation.Trim();
                }
            }

            return string.Join("/", segments);
        }

        private static ApplyResult Failure(string message)
        {
            return new ApplyResult { Success = false, Message = message };
        }
    }
}
#endif
