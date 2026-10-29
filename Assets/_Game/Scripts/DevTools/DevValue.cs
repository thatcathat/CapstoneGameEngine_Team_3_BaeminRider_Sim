using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace DeliveryRider
{
    // One tunable number or toggle inside a component or settings asset, reached through its serialized field path:
    // a field, an element of a short array, one part of a vector, or a field of a short array of plain entries.
    // Raw values travel as floats (ints round, bools are 0/1); the panel may show them converted (jump height).
    public sealed class DevValue
    {
        // Longer arrays are baked data (paths, tables, candidates), not tuning values.
        private const int MaxListedElements = 8;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly struct Step
        {
            public readonly FieldInfo Field;
            public readonly int Index;
            public Step(FieldInfo field) { Field = field; Index = -1; }
            public Step(int index) { Field = null; Index = index; }
        }

        private readonly Step[] path;
        private readonly Type leaf;
        private readonly bool hardMin, hardMax;
        private readonly Func<float, float> toDisplay, fromDisplay;

        public string Label { get; }
        public string Tooltip { get; }
        public string Header { get; private set; }
        public string PropertyPath { get; }
        // Slider range in display units; only Range and Min attributes (or a conversion) clamp typed values.
        public float Min { get; }
        public float Max { get; }
        public bool IsInt => leaf == typeof(int);
        public bool IsBool => leaf == typeof(bool);

        private DevValue(Step[] path, Type leaf, string propertyPath, string label, string tooltip, string header,
            float min, float max, bool hardMin, bool hardMax, Func<float, float> toDisplay = null, Func<float, float> fromDisplay = null)
        {
            this.path = path;
            this.leaf = leaf;
            PropertyPath = propertyPath;
            Label = label;
            Tooltip = tooltip;
            Header = header;
            Min = min;
            Max = max;
            this.hardMin = hardMin;
            this.hardMax = hardMax;
            this.toDisplay = toDisplay;
            this.fromDisplay = fromDisplay;
        }

        public float Read(object root)
        {
            object value = root;
            foreach (var step in path) value = Get(value, step);
            return ToFloat(value);
        }

        public void Write(object root, float raw) => Set(root, 0, IsBool ? raw >= 0.5f : IsInt ? Mathf.RoundToInt(raw) : (object)raw);
        public float ToDisplay(float raw) => toDisplay != null ? toDisplay(raw) : raw;

        public float FromDisplay(float display)
        {
            if (hardMin) display = Mathf.Max(display, Min);
            if (hardMax) display = Mathf.Min(display, Max);
            float raw = fromDisplay != null ? fromDisplay(display) : display;
            return IsInt ? Mathf.Round(raw) : raw;
        }

        public string Format(float display) => IsInt ? Mathf.RoundToInt(display).ToString(CultureInfo.InvariantCulture)
            : display.ToString(Mathf.Abs(display) < 0.1f ? "0.#####" : "0.###", CultureInfo.InvariantCulture);

        // The same field shown in other units, e.g. jump speed as jump height.
        public DevValue Converted(string label, Func<float, float> toDisplayUnits, Func<float, float> fromDisplayUnits, float min, float max)
            => new DevValue(path, leaf, PropertyPath, label, Tooltip, Header, min, max, true, true, toDisplayUnits, fromDisplayUnits);

        // Every tunable value of a component or asset, base types first, in declaration order.
        public static List<DevValue> Discover(UnityEngine.Object sample)
        {
            var values = new List<DevValue>();
            Collect(sample.GetType(), sample, new List<Step>(), "", "", true, values);
            return values;
        }

        // A value at a known serialized path, e.g. "kinds.Array.data[2].poolSize".
        public static DevValue Find(UnityEngine.Object sample, string propertyPath, string label)
        {
            var steps = new List<Step>();
            object current = sample;
            var type = sample.GetType();
            FieldInfo owner = null;
            var parts = propertyPath.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "Array")
                {
                    string data = parts[++i];
                    int index = int.Parse(data.Substring(5, data.Length - 6), CultureInfo.InvariantCulture);
                    steps.Add(new Step(index));
                    current = ((Array)current).GetValue(index);
                    type = type.GetElementType();
                    continue;
                }
                owner = FindField(type, parts[i]) ?? throw new ArgumentException($"{type.Name} has no field {parts[i]}.");
                steps.Add(new Step(owner));
                current = owner.GetValue(current);
                type = owner.FieldType;
            }
            if (!IsLeaf(type)) throw new ArgumentException($"{propertyPath} is not a number or toggle.");
            return Leaf(steps, type, propertyPath, label, owner, current);
        }

        private static void Collect(Type type, object instance, List<Step> prefix, string pathPrefix, string labelPrefix, bool top, List<DevValue> into)
        {
            foreach (var field in SerializedFields(type))
            {
                var value = field.GetValue(instance);
                var fieldType = field.FieldType;
                string propertyPath = pathPrefix + field.Name;
                string label = labelPrefix + (DevLabels.Find(type.Name, field.Name) ?? Nicify(field.Name));
                int first = into.Count;
                prefix.Add(new Step(field));
                if (IsLeaf(fieldType)) into.Add(Leaf(prefix, fieldType, propertyPath, label, field, value));
                else if (fieldType == typeof(Vector2) || fieldType == typeof(Vector2Int))
                {
                    // Vector2Int keeps its parts in m_X/m_Y; the serialized names are x/y either way.
                    bool ints = fieldType == typeof(Vector2Int);
                    foreach (var (axis, backing) in new[] { ("x", ints ? "m_X" : "x"), ("y", ints ? "m_Y" : "y") })
                    {
                        var part = fieldType.GetField(backing, AnyInstance);
                        prefix.Add(new Step(part));
                        into.Add(Leaf(prefix, part.FieldType, $"{propertyPath}.{axis}", $"{label} ({axis})", field, part.GetValue(value)));
                        prefix.RemoveAt(prefix.Count - 1);
                    }
                }
                else if (fieldType.IsArray && value is Array array && array.Length <= MaxListedElements)
                {
                    var element = fieldType.GetElementType();
                    for (int i = 0; i < array.Length; i++)
                    {
                        prefix.Add(new Step(i));
                        string elementPath = $"{propertyPath}.Array.data[{i}]", elementLabel = $"{label} [{i + 1}]";
                        if (IsLeaf(element)) into.Add(Leaf(prefix, element, elementPath, elementLabel, field, array.GetValue(i)));
                        // One level of plain entries (reward tiers, vehicle shares); deeper data is layout, not tuning.
                        else if (top && IsPlainEntry(element)) Collect(element, array.GetValue(i), prefix, elementPath + ".", elementLabel + " ", false, into);
                        prefix.RemoveAt(prefix.Count - 1);
                    }
                }
                prefix.RemoveAt(prefix.Count - 1);
                var header = top ? field.GetCustomAttribute<HeaderAttribute>() : null;
                if (header != null && into.Count > first) into[first].Header = header.header;
            }
        }

        private static DevValue Leaf(List<Step> steps, Type leaf, string propertyPath, string label, FieldInfo field, object current)
        {
            var range = field.GetCustomAttribute<RangeAttribute>();
            var minimum = field.GetCustomAttribute<MinAttribute>();
            string tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
            float value = ToFloat(current);
            bool isInt = leaf == typeof(int);
            // Without attributes the slider spans three times the current value; typed values may go beyond it.
            float min = range != null ? range.min : minimum != null ? minimum.min : value < 0f ? value * 3f : 0f;
            float max = range != null ? range.max : Mathf.Max(Mathf.Abs(value) * 3f, min + (isInt ? 10f : 1f));
            return new DevValue(steps.ToArray(), leaf, propertyPath, label, tooltip != null ? $"{propertyPath} — {tooltip}" : propertyPath,
                null, min, max, range != null || minimum != null, range != null);
        }

        private static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
                chain.Insert(0, t);
            foreach (var t in chain)
                foreach (var field in t.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
                    if (!field.IsInitOnly && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), false))
                        && !field.IsDefined(typeof(HideInInspector), false))
                        yield return field;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, AnyInstance | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static bool IsLeaf(Type type) => type == typeof(float) || type == typeof(int) || type == typeof(bool);
        private static bool IsPlainEntry(Type type) => type.Namespace == nameof(DeliveryRider) && type.IsSerializable
            && !typeof(UnityEngine.Object).IsAssignableFrom(type) && !type.IsEnum;
        private static float ToFloat(object value) => value is bool on ? (on ? 1f : 0f) : value is int whole ? whole : (float)value;
        private static object Get(object container, Step step) => step.Field != null ? step.Field.GetValue(container) : ((Array)container).GetValue(step.Index);

        private void Set(object container, int index, object value)
        {
            var step = path[index];
            if (index == path.Length - 1) { Put(container, step, value); return; }
            var child = Get(container, step);
            Set(child, index + 1, value);
            // Vectors and struct entries are copies: store the changed copy back.
            if (child is ValueType) Put(container, step, child);
        }

        private static void Put(object container, Step step, object value)
        {
            if (step.Field != null) step.Field.SetValue(container, value);
            else ((Array)container).SetValue(value, step.Index);
        }

        // "maximumLaunchSpeed" -> "Maximum Launch Speed", as the Inspector shows it.
        private static string Nicify(string name)
        {
            var text = new StringBuilder(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i == 0) { text.Append(char.ToUpperInvariant(c)); continue; }
                if (char.IsUpper(c) && !char.IsUpper(name[i - 1])) text.Append(' ');
                text.Append(c);
            }
            return text.ToString();
        }
    }
}
