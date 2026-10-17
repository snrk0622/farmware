// このファイルは、DefinitionCatalogAuthoringの動物種配列を名前付きで表示する専用Inspectorです。
// 共通定義の表示名をGUIDで解決し、動物種固有設定へ重複した名前データを持たせずに編集しやすくします。

using System;
using Farmware.Authoring;
using UnityEditor;
using UnityEngine;

namespace Farmware.WorldGeneration.Editor
{
    /// <summary>
    /// DefinitionCatalogAuthoringの通常のシリアライズ編集を保ちながら、
    /// Animal Species Definitionsの各要素へ対応する共通定義名を表示する専用Inspectorです。
    /// </summary>
    [CustomEditor(typeof(DefinitionCatalogAuthoring))]
    public sealed class DefinitionCatalogAuthoringEditor : UnityEditor.Editor
    {
        // UnityがMonoBehaviourのScript参照に使用する予約済みProperty名です。
        private const string ScriptPropertyName = "m_Script";

        // 共通定義から動物名を検索するための配列Property名です。
        private const string DefinitionsPropertyName = "definitions";

        // 名前付きの見出しへ差し替える動物種設定配列のProperty名です。
        private const string AnimalSpeciesDefinitionsPropertyName =
            "animalSpeciesDefinitions";

        // 共通定義内でInspector表示名を保持するProperty名です。
        private const string DisplayNamePropertyName = "displayName";

        // 共通定義内で永続GUIDを保持するProperty名です。
        private const string DefinitionGuidPropertyName = "guid";

        // 動物種設定から共通定義を参照するProperty名です。
        private const string AnimalDefinitionGuidPropertyName = "definitionGuid";

        /// <summary>
        /// UnityがInspectorを再描画するときに呼び出し、通常項目と名前付き動物種配列を表示します。
        /// SerializedProperty経由で編集するため、Undo、Prefab差分、SceneのDirty管理はUnity標準処理へ委ねます。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty definitions =
                serializedObject.FindProperty(DefinitionsPropertyName);
            SerializedProperty propertyIterator = serializedObject.GetIterator();
            bool enterChildren = true;

            // 将来フィールドが追加されても専用Inspectorから消えないよう、全可視Propertyを順番に走査します。
            while (propertyIterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                SerializedProperty currentProperty = propertyIterator.Copy();
                if (currentProperty.propertyPath ==
                    AnimalSpeciesDefinitionsPropertyName)
                {
                    DrawAnimalSpeciesDefinitions(definitions, currentProperty);
                    continue;
                }

                // Script参照は型の識別用であり、誤って別Scriptへ差し替えないよう読み取り専用にします。
                using (new EditorGUI.DisabledScope(
                    currentProperty.propertyPath == ScriptPropertyName))
                {
                    EditorGUILayout.PropertyField(currentProperty, true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 動物種設定のGUIDに対応する共通定義名を検索し、配列要素の見出しを作成します。
        /// Editorテストからも呼び出し、名前解決と未解決時の表示が維持されることを保証します。
        /// </summary>
        /// <param name="definitions">表示名とGUIDを保持する共通定義配列です。</param>
        /// <param name="animalSpecies">見出しを作る1件の動物種固有設定です。</param>
        /// <param name="elementIndex">Inspector配列内の0始まりの位置です。</param>
        /// <returns>例として「0: Chicken」、未解決なら原因を含む日本語の見出しです。</returns>
        public static string BuildAnimalSpeciesElementLabel(
            SerializedProperty definitions,
            SerializedProperty animalSpecies,
            int elementIndex)
        {
            string indexLabel = elementIndex.ToString();
            SerializedProperty animalGuidProperty = animalSpecies?.FindPropertyRelative(
                AnimalDefinitionGuidPropertyName);
            string animalGuid = animalGuidProperty?.stringValue;
            if (string.IsNullOrWhiteSpace(animalGuid))
            {
                return $"{indexLabel}: 名前未解決（GUID未設定）";
            }

            if (definitions != null && definitions.isArray)
            {
                // 配列添字は種類追加や並べ替えで変わるため、永続GUIDの一致だけを名前解決に使います。
                for (int definitionIndex = 0;
                     definitionIndex < definitions.arraySize;
                     definitionIndex++)
                {
                    SerializedProperty definition =
                        definitions.GetArrayElementAtIndex(definitionIndex);
                    string definitionGuid = definition.FindPropertyRelative(
                        DefinitionGuidPropertyName)?.stringValue;
                    if (!AreEquivalentDefinitionGuids(animalGuid, definitionGuid))
                    {
                        continue;
                    }

                    string displayName = definition.FindPropertyRelative(
                        DisplayNamePropertyName)?.stringValue;
                    return string.IsNullOrWhiteSpace(displayName)
                        ? $"{indexLabel}: 名前未設定"
                        : $"{indexLabel}: {displayName.Trim()}";
                }
            }

            return $"{indexLabel}: 名前未解決（{CreateShortGuid(animalGuid)}）";
        }

        /// <summary>
        /// 動物種配列の折りたたみ、件数、各要素を描画し、要素見出しだけを解決済みの動物名へ置き換えます。
        /// </summary>
        /// <param name="definitions">動物名の検索元となる共通定義配列です。</param>
        /// <param name="animalSpeciesDefinitions">描画対象の動物種固有設定配列です。</param>
        private static void DrawAnimalSpeciesDefinitions(
            SerializedProperty definitions,
            SerializedProperty animalSpeciesDefinitions)
        {
            GUIContent arrayLabel = new GUIContent(
                animalSpeciesDefinitions.displayName,
                animalSpeciesDefinitions.tooltip);
            animalSpeciesDefinitions.isExpanded = EditorGUILayout.Foldout(
                animalSpeciesDefinitions.isExpanded,
                arrayLabel,
                true);
            if (!animalSpeciesDefinitions.isExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                int requestedSize = EditorGUILayout.DelayedIntField(
                    "Size",
                    animalSpeciesDefinitions.arraySize);
                if (requestedSize != animalSpeciesDefinitions.arraySize)
                {
                    // Unityの配列サイズは負数を持てないため、手入力された負数は空配列として扱います。
                    animalSpeciesDefinitions.arraySize = Mathf.Max(0, requestedSize);
                }

                for (int animalIndex = 0;
                     animalIndex < animalSpeciesDefinitions.arraySize;
                     animalIndex++)
                {
                    SerializedProperty animalSpecies =
                        animalSpeciesDefinitions.GetArrayElementAtIndex(animalIndex);
                    string elementLabel = BuildAnimalSpeciesElementLabel(
                        definitions,
                        animalSpecies,
                        animalIndex);
                    EditorGUILayout.PropertyField(
                        animalSpecies,
                        new GUIContent(elementLabel),
                        true);
                }
            }
        }

        /// <summary>
        /// 32桁形式とハイフン付き形式を同じGUIDとして比較し、Inspector上の表記差で名前解決が失敗しないようにします。
        /// </summary>
        /// <param name="left">比較する動物種側のGUID文字列です。</param>
        /// <param name="right">比較する共通定義側のGUID文字列です。</param>
        /// <returns>両方が有効な同一GUID、または同じ文字列である場合はtrueです。</returns>
        private static bool AreEquivalentDefinitionGuids(string left, string right)
        {
            if (Guid.TryParse(left, out Guid leftGuid)
                && Guid.TryParse(right, out Guid rightGuid))
            {
                return leftGuid == rightGuid;
            }

            return string.Equals(
                left?.Trim(),
                right?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 名前を解決できない要素でも参照先を見分けられるよう、GUIDの先頭8文字を表示用に切り出します。
        /// </summary>
        /// <param name="guid">短縮表示するGUID文字列です。</param>
        /// <returns>空白とハイフンを除いた先頭8文字、またはGUID未設定を示す文言です。</returns>
        private static string CreateShortGuid(string guid)
        {
            string compactGuid = guid?.Trim().Replace("-", string.Empty);
            if (string.IsNullOrEmpty(compactGuid))
            {
                return "GUID未設定";
            }

            return compactGuid.Length <= 8
                ? compactGuid
                : compactGuid.Substring(0, 8);
        }
    }
}
