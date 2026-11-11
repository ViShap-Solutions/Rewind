using System.Reflection;
using System.Text;

namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>
/// Renders the exported surface of an assembly as text, one line per type and one indented line per member, in the
/// notation of the public surface listing: C# keywords, short type names, nullable annotations, parameter names.
/// </summary>
public static class PublicApi
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<Type, string> s_keywords = new()
    {
        [typeof(void)] = "void",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char",
        [typeof(short)] = "short",
        [typeof(ushort)] = "ushort",
        [typeof(int)] = "int",
        [typeof(uint)] = "uint",
        [typeof(long)] = "long",
        [typeof(ulong)] = "ulong",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
    };

    /// <summary>Returns the lines of every exported type of <paramref name="assembly"/>, each type followed by its members.</summary>
    public static List<string> Render(Assembly assembly)
    {
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            lines.Add(TypeHeader(type));
            lines.AddRange(Members(type).Select(member => "    " + member));
        }

        return lines;
    }

    /// <summary>Returns the line that declares <paramref name="type"/>: its kind, name, base type and interfaces.</summary>
    public static string TypeHeader(Type type)
    {
        string kind;
        if (type.IsInterface)
        {
            kind = "interface";
        }
        else if (type.IsEnum)
        {
            kind = "enum";
        }
        else if (type.IsValueType)
        {
            kind = "struct";
        }
        else if (type.IsAbstract && type.IsSealed)
        {
            kind = "static class";
        }
        else if (type.IsSealed)
        {
            kind = "sealed class";
        }
        else if (type.IsAbstract)
        {
            kind = "abstract class";
        }
        else
        {
            kind = "class";
        }

        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && !type.IsEnum)
        {
            bases.Add(TypeName(baseType, null));
        }

        var inherited = type.BaseType?.GetInterfaces() ?? [];
        var declared = type.GetInterfaces().Where(candidate => !inherited.Contains(candidate)).ToList();
        var indirect = declared.SelectMany(candidate => candidate.GetInterfaces()).ToHashSet();
        bases.AddRange(declared
            .Where(candidate => !indirect.Contains(candidate))
            .Select(candidate => TypeName(candidate, null))
            .OrderBy(name => name, StringComparer.Ordinal));

        var header = $"{kind} {ShortName(type)}{GenericParameters(type)}";
        return bases.Count == 0 ? header : $"{header} : {string.Join(", ", bases)}";
    }

    /// <summary>Returns the members of <paramref name="type"/> visible outside its assembly, one line each.</summary>
    public static IEnumerable<string> Members(Type type)
    {
        var context = new NullabilityInfoContext();
        var implicitStatic = type.IsAbstract && type.IsSealed;

        foreach (var constructor in type.GetConstructors(Declared).Where(IsVisible))
        {
            yield return $"{Modifiers(constructor, implicitStatic)}{ShortName(type)}({Parameters(constructor, context)})";
        }

        foreach (var property in type.GetProperties(Declared).Where(property => property.GetAccessors(true).Any(IsVisible)))
        {
            var accessors = new StringBuilder("{");
            if (property.GetMethod is { } getter && IsVisible(getter))
            {
                accessors.Append(" get;");
            }

            if (property.SetMethod is { } setter && IsVisible(setter))
            {
                var init = setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.Name == "IsExternalInit");
                accessors.Append(init ? " init;" : " set;");
            }

            accessors.Append(" }");
            var accessor = property.GetAccessors(true).First(IsVisible);
            yield return $"{Modifiers(accessor, implicitStatic)}{TypeName(property.PropertyType, context.Create(property))} {property.Name} {accessors}";
        }

        foreach (var method in type.GetMethods(Declared).Where(method => IsVisible(method) && !method.IsSpecialName))
        {
            var generics = method.IsGenericMethodDefinition
                ? "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">"
                : string.Empty;
            var returnType = TypeName(method.ReturnType, context.Create(method.ReturnParameter));
            yield return $"{Modifiers(method, implicitStatic)}{returnType} {method.Name}{generics}({Parameters(method, context)}){Constraints(method)}";
        }
    }

    private static bool IsVisible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static string Modifiers(MethodBase method, bool implicitStatic)
    {
        var modifiers = new StringBuilder();
        if (method.IsFamily || method.IsFamilyOrAssembly)
        {
            modifiers.Append("protected ");
        }

        if (method.IsStatic && !implicitStatic)
        {
            modifiers.Append("static ");
        }

        return modifiers.ToString();
    }

    private static string Parameters(MethodBase method, NullabilityInfoContext context) =>
        string.Join(", ", method.GetParameters().Select(parameter =>
        {
            var type = parameter.ParameterType;
            var prefix = string.Empty;
            if (type.IsByRef)
            {
                prefix = parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ";
                type = type.GetElementType()!;
            }
            else if (parameter.IsDefined(typeof(ParamArrayAttribute)))
            {
                prefix = "params ";
            }

            var info = parameter.ParameterType.IsByRef ? null : context.Create(parameter);
            return $"{prefix}{TypeName(type, info)} {parameter.Name}";
        }));

    private static string Constraints(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition)
        {
            return string.Empty;
        }

        var clauses = new StringBuilder();
        foreach (var argument in method.GetGenericArguments())
        {
            var parts = new List<string>();
            var attributes = argument.GenericParameterAttributes;
            if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                parts.Add("class");
            }

            if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
            {
                parts.Add("struct");
            }

            parts.AddRange(argument.GetGenericParameterConstraints()
                .Where(constraint => constraint != typeof(ValueType))
                .Select(constraint => TypeName(constraint, null)));
            if (attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint)
                && !attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
            {
                parts.Add("new()");
            }

            if (parts.Count > 0)
            {
                clauses.Append($" where {argument.Name} : {string.Join(", ", parts)}");
            }
        }

        return clauses.ToString();
    }

    private static string GenericParameters(Type type) =>
        type.IsGenericTypeDefinition
            ? "<" + string.Join(", ", type.GetGenericArguments().Select(argument => argument.Name)) + ">"
            : string.Empty;

    private static string ShortName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    private static string TypeName(Type type, NullabilityInfo? info)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TypeName(underlying, info?.GenericTypeArguments.FirstOrDefault()) + "?";
        }

        string name;
        if (type.IsArray)
        {
            name = TypeName(type.GetElementType()!, info?.ElementType) + "[]";
        }
        else if (s_keywords.TryGetValue(type, out var keyword))
        {
            name = keyword;
        }
        else if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var argumentNames = arguments.Select((argument, index) =>
                TypeName(argument, info is not null && index < info.GenericTypeArguments.Length ? info.GenericTypeArguments[index] : null));
            name = $"{ShortName(type)}<{string.Join(", ", argumentNames)}>";
        }
        else
        {
            name = ShortName(type);
        }

        // An unconstrained type parameter reads as nullable whether or not it is annotated, so the listing never
        // annotates a type parameter.
        var nullable = info is not null && !type.IsValueType && !type.IsGenericParameter && info.ReadState == NullabilityState.Nullable;
        return nullable ? name + "?" : name;
    }
}
