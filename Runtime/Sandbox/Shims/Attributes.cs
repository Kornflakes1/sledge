// Stand-ins for s&box editor/reflection attributes so the ported code compiles unchanged.
// They carry no behaviour in Unity.
namespace Sandbox;

[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class TitleAttribute : Attribute { public TitleAttribute( string value ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class GroupAttribute : Attribute { public GroupAttribute( string value ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class IconAttribute : Attribute { public IconAttribute( string value ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class DescriptionAttribute : Attribute { public DescriptionAttribute( string value ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class ActionGraphNodeAttribute : Attribute { public ActionGraphNodeAttribute( string value ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class ActionGraphIncludeAttribute : Attribute { public bool AutoExpand { get; set; } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class PureAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class ExposeAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class JsonIgnoreAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class JsonIncludeAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class JsonPropertyNameAttribute : Attribute { public JsonPropertyNameAttribute( string name ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class RangeAttribute : Attribute { public RangeAttribute( float min, float max, float step = 0.01f, bool clamped = true, bool slider = true ) { } }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class HideAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class WideModeAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class EnumButtonGroupAttribute : Attribute { }
[AttributeUsage( AttributeTargets.All, AllowMultiple = true )] internal sealed class EditorAttribute : Attribute { public EditorAttribute( string value ) { } }
