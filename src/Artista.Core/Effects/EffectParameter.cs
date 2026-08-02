namespace Artista.Core.Effects;

/// <summary>
/// Declarative description of one effect parameter. Effect dialogs build their
/// UI automatically from these, so a new effect never needs custom dialog code.
/// </summary>
public abstract class EffectParameter
{
    public string Id { get; }
    public string Label { get; }

    protected EffectParameter(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public abstract object DefaultValue { get; }
}

public sealed class IntParameter : EffectParameter
{
    public int Min { get; }
    public int Max { get; }
    public int Default { get; }
    public string? Unit { get; }

    public IntParameter(string id, string label, int min, int max, int @default, string? unit = null)
        : base(id, label)
    {
        Min = min; Max = max; Default = @default; Unit = unit;
    }

    public override object DefaultValue => Default;
}

public sealed class DoubleParameter : EffectParameter
{
    public double Min { get; }
    public double Max { get; }
    public double Default { get; }
    public int Decimals { get; }

    public DoubleParameter(string id, string label, double min, double max, double @default, int decimals = 2)
        : base(id, label)
    {
        Min = min; Max = max; Default = @default; Decimals = decimals;
    }

    public override object DefaultValue => Default;
}

public sealed class BoolParameter : EffectParameter
{
    public bool Default { get; }

    public BoolParameter(string id, string label, bool @default) : base(id, label) => Default = @default;

    public override object DefaultValue => Default;
}

/// <summary>Color value stored as packed BGRA uint.</summary>
public sealed class ColorParameter : EffectParameter
{
    public uint Default { get; }

    /// <summary>When true, the host dialog offers an eyedropper to sample from the canvas.</summary>
    public bool AllowEyedropper { get; }

    public ColorParameter(string id, string label, uint @default, bool allowEyedropper = false)
        : base(id, label)
    {
        Default = @default;
        AllowEyedropper = allowEyedropper;
    }

    public override object DefaultValue => Default;
}

public sealed class EnumParameter : EffectParameter
{
    public IReadOnlyList<string> Options { get; }
    public int DefaultIndex { get; }

    public EnumParameter(string id, string label, IReadOnlyList<string> options, int defaultIndex = 0)
        : base(id, label)
    {
        Options = options;
        DefaultIndex = defaultIndex;
    }

    public override object DefaultValue => DefaultIndex;
}

/// <summary>
/// A tone curve: control points per channel mapped to a 256-entry lookup table.
/// The host dialog shows an editable curve control for this parameter type.
/// </summary>
public sealed class CurvesParameter : EffectParameter
{
    public CurvesParameter(string id, string label) : base(id, label) { }

    public override object DefaultValue => CurvesValue.Identity();
}

/// <summary>Values captured for one effect invocation, keyed by parameter id.</summary>
public sealed class ParameterSet
{
    private readonly Dictionary<string, object> _values = new();

    public static ParameterSet FromDefaults(IEnumerable<EffectParameter> parameters)
    {
        var set = new ParameterSet();
        foreach (var p in parameters)
            set._values[p.Id] = p.DefaultValue;
        return set;
    }

    public ParameterSet Clone()
    {
        var set = new ParameterSet();
        foreach (var (k, v) in _values)
            set._values[k] = v;
        return set;
    }

    public void Set(string id, object value) => _values[id] = value;

    public int GetInt(string id) => Convert.ToInt32(_values[id]);
    public double GetDouble(string id) => Convert.ToDouble(_values[id]);
    public bool GetBool(string id) => (bool)_values[id];
    public uint GetColor(string id) => Convert.ToUInt32(_values[id]);
    public int GetEnum(string id) => Convert.ToInt32(_values[id]);
    public CurvesValue GetCurves(string id) => (CurvesValue)_values[id];
}
