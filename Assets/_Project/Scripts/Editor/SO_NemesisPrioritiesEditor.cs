using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;

/// <summary>
/// Inspector for <see cref="SO_NemesisPriorities"/>: the ladder as a list you drag.
///
/// The default inspector already gives reorderable lists, so this exists for one reason — a rung
/// collapsed to "Element 3" is unreadable, and the whole point of moving the ladder into an asset
/// was that a designer can see the priority order at a glance and change it. Each row here reads
/// as the sentence the rung actually is: "5. Chasing ← Sees Player".
///
/// Labels in Spanish to match the other custom inspectors in this project.
/// </summary>
[CustomEditor(typeof(SO_NemesisPriorities))]
public class SO_NemesisPrioritiesEditor : Editor
{
    private ReorderableList list;
    private SerializedProperty rungs;
    private SerializedProperty minimumStateDwell;

    private void OnEnable()
    {
        rungs = serializedObject.FindProperty("rungs");
        minimumStateDwell = serializedObject.FindProperty("minimumStateDwell");

        list = new ReorderableList(serializedObject, rungs, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(
                rect, "Escalera de prioridades  ·  gana la primera regla que se cumpla"),
            elementHeightCallback = ElementHeight,
            drawElementCallback = DrawElement,
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Se lee de arriba hacia abajo. La primera regla cuyas condiciones se cumplan TODAS " +
            "decide el estado; las de abajo ni se consultan.\n\n" +
            "Los umbrales de tiempo salen de SO_NemesisData: elegí el campo por nombre en vez de " +
            "escribir un número, así el valor sigue viviendo en un solo lugar.",
            MessageType.Info);

        EditorGUILayout.PropertyField(minimumStateDwell);
        EditorGUILayout.Space();

        list.DoLayoutList();

        EditorGUILayout.Space();
        DrawSafetyNetWarning();
        DrawRestoreButton();

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// A ladder whose last rung has conditions can fall through to "stay where you are", which on
    /// a Nemesis that has just lost its target reads as one that froze. Cheap to check here, and
    /// invisible until it happens in play.
    /// </summary>
    private void DrawSafetyNetWarning()
    {
        if (EndsUnconditionally()) return;

        EditorGUILayout.HelpBox(
            "La última regla tiene condiciones. Si ninguna regla se cumple, el Nemesis se queda " +
            "en el estado en el que está en vez de volver a patrullar. Dejá abajo de todo una " +
            "regla sin condiciones (normalmente Patrolling).",
            MessageType.Warning);
    }

    private void DrawRestoreButton()
    {
        if (!GUILayout.Button("Restaurar la escalera por defecto")) return;

        bool confirmed = EditorUtility.DisplayDialog(
            "Restaurar la escalera",
            "Reemplaza TODAS las reglas por las que trae el juego de fábrica. Se pierde lo que " +
            "hayas reordenado o agregado.",
            "Restaurar", "Cancelar");

        if (!confirmed) return;

        var defaults = SO_NemesisPriorities.BuildDefaultLadder();
        rungs.arraySize = defaults.Count;

        for (int i = 0; i < defaults.Count; i++)
        {
            WriteRung(rungs.GetArrayElementAtIndex(i), defaults[i]);
        }

        // Nothing else to do: OnInspectorGUI applies the serializedObject on the way out, which
        // registers the undo step and marks the asset dirty for us.
    }

    private static void WriteRung(SerializedProperty target, NemesisPriorityRung source)
    {
        target.FindPropertyRelative("enabled").boolValue = source.enabled;
        target.FindPropertyRelative("target").enumValueIndex = (int)source.target;
        target.FindPropertyRelative("interrupts").boolValue = source.interrupts;
        target.FindPropertyRelative("note").stringValue = source.note;

        SerializedProperty conditions = target.FindPropertyRelative("conditions");
        conditions.arraySize = source.conditions.Count;

        for (int i = 0; i < source.conditions.Count; i++)
        {
            NemesisCondition condition = source.conditions[i];
            SerializedProperty element = conditions.GetArrayElementAtIndex(i);

            element.FindPropertyRelative("predicate").enumValueIndex = (int)condition.predicate;
            element.FindPropertyRelative("negate").boolValue = condition.negate;
            element.FindPropertyRelative("state").enumValueIndex = (int)condition.state;
            element.FindPropertyRelative("threshold").enumValueIndex = (int)condition.threshold;
            element.FindPropertyRelative("customSeconds").floatValue = condition.customSeconds;
        }
    }

    private bool EndsUnconditionally()
    {
        if (rungs.arraySize == 0) return false;

        SerializedProperty last = rungs.GetArrayElementAtIndex(rungs.arraySize - 1);
        return last.FindPropertyRelative("enabled").boolValue &&
               last.FindPropertyRelative("conditions").arraySize == 0;
    }

    private float ElementHeight(int index) =>
        EditorGUI.GetPropertyHeight(rungs.GetArrayElementAtIndex(index), true) + 6f;

    private void DrawElement(Rect rect, int index, bool active, bool focused)
    {
        SerializedProperty element = rungs.GetArrayElementAtIndex(index);

        rect.y += 3f;
        rect.height = EditorGUI.GetPropertyHeight(element, true);

        EditorGUI.PropertyField(rect, element, Summarise(element, index), true);
    }

    /// <summary>
    /// The one-line sentence a collapsed rung shows: its rank, the state it asks for, and the
    /// conditions that have to hold. This is the whole reason for the custom inspector.
    /// </summary>
    private static GUIContent Summarise(SerializedProperty element, int index)
    {
        bool enabled = element.FindPropertyRelative("enabled").boolValue;
        SerializedProperty targetState = element.FindPropertyRelative("target");
        SerializedProperty conditions = element.FindPropertyRelative("conditions");

        StringBuilder text = new StringBuilder();
        text.Append(index + 1).Append(". ");
        if (!enabled) text.Append("(apagada) ");
        text.Append(targetState.enumDisplayNames[targetState.enumValueIndex]);

        // The bolt marks a rung that ignores the hysteresis window. Worth seeing without
        // expanding: it is the difference between "reacts instantly" and "reacts in a third of a
        // second", and marking every rung is how a designer accidentally turns the window off.
        if (element.FindPropertyRelative("interrupts").boolValue) text.Append(" ⚡");

        if (conditions.arraySize == 0)
        {
            text.Append("  ←  siempre");
        }
        else
        {
            text.Append("  ←  ");
            for (int i = 0; i < conditions.arraySize; i++)
            {
                if (i > 0) text.Append(" · ");
                text.Append(DescribeCondition(conditions.GetArrayElementAtIndex(i)));
            }
        }

        string note = element.FindPropertyRelative("note").stringValue;
        return new GUIContent(text.ToString(), string.IsNullOrEmpty(note) ? null : note);
    }

    private static string DescribeCondition(SerializedProperty condition)
    {
        SerializedProperty predicate = condition.FindPropertyRelative("predicate");
        string name = predicate.enumDisplayNames[predicate.enumValueIndex];

        if (predicate.enumValueIndex == (int)ENemesisPredicate.IsInState)
        {
            SerializedProperty state = condition.FindPropertyRelative("state");
            name = $"está en {state.enumDisplayNames[state.enumValueIndex]}";
        }
        else if (predicate.enumValueIndex == (int)ENemesisPredicate.BeliefAgeUnder ||
                 predicate.enumValueIndex == (int)ENemesisPredicate.TimeInStateUnder)
        {
            SerializedProperty threshold = condition.FindPropertyRelative("threshold");

            name += threshold.enumValueIndex == (int)ENemesisThreshold.Custom
                ? $" {condition.FindPropertyRelative("customSeconds").floatValue:0.##}s"
                : $" {threshold.enumDisplayNames[threshold.enumValueIndex]}";
        }

        return condition.FindPropertyRelative("negate").boolValue ? $"NO {name}" : name;
    }
}

/// <summary>
/// Draws one <see cref="NemesisCondition"/> as a single line, showing only the fields the chosen
/// predicate actually reads.
///
/// The struct carries a field per predicate shape because Unity serialises polymorphic lists only
/// through SerializeReference, which breaks a designer's ladder whenever a class is renamed. The
/// price of that decision is fields that mean nothing most of the time — a "Custom Seconds" under
/// "Sees Player" is not a setting, it is a trap. This is where that price gets paid back.
/// </summary>
[CustomPropertyDrawer(typeof(NemesisCondition))]
public class NemesisConditionDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
        EditorGUIUtility.singleLineHeight;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        SerializedProperty predicate = property.FindPropertyRelative("predicate");
        SerializedProperty negate = property.FindPropertyRelative("negate");

        const float toggleWidth = 34f;
        const float gap = 4f;

        Rect toggleRect = new Rect(position.x, position.y, toggleWidth, position.height);
        int predicateValue = predicate.enumValueIndex;

        bool usesState = predicateValue == (int)ENemesisPredicate.IsInState;
        bool usesThreshold = predicateValue == (int)ENemesisPredicate.BeliefAgeUnder ||
                             predicateValue == (int)ENemesisPredicate.TimeInStateUnder;

        float remaining = position.width - toggleWidth - gap;
        float predicateWidth = usesState || usesThreshold ? remaining * 0.55f : remaining;

        Rect predicateRect = new Rect(toggleRect.xMax + gap, position.y, predicateWidth,
                                      position.height);
        Rect extraRect = new Rect(predicateRect.xMax + gap, position.y,
                                  remaining - predicateWidth - gap, position.height);

        // "no" rather than a bare checkbox: an unlabelled tick in front of a condition reads as
        // "enabled", which is the opposite of what it does.
        negate.boolValue = EditorGUI.ToggleLeft(toggleRect, "no", negate.boolValue);
        EditorGUI.PropertyField(predicateRect, predicate, GUIContent.none);

        if (usesState)
        {
            EditorGUI.PropertyField(extraRect, property.FindPropertyRelative("state"),
                                    GUIContent.none);
        }
        else if (usesThreshold)
        {
            SerializedProperty threshold = property.FindPropertyRelative("threshold");

            if (threshold.enumValueIndex == (int)ENemesisThreshold.Custom)
            {
                float half = (extraRect.width - gap) * 0.5f;
                Rect thresholdRect = new Rect(extraRect.x, extraRect.y, half, extraRect.height);
                Rect secondsRect = new Rect(thresholdRect.xMax + gap, extraRect.y, half,
                                            extraRect.height);

                EditorGUI.PropertyField(thresholdRect, threshold, GUIContent.none);
                EditorGUI.PropertyField(secondsRect,
                                        property.FindPropertyRelative("customSeconds"),
                                        GUIContent.none);
            }
            else
            {
                EditorGUI.PropertyField(extraRect, threshold, GUIContent.none);
            }
        }

        EditorGUI.EndProperty();
    }
}
#endif
