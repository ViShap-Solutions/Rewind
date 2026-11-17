using System.Reflection;
using System.Text;

namespace ViShap.Rewind.Tests.Fixtures;

/// <summary>Computes the identifiers the C# compiler writes into an XML documentation file.</summary>
public static class DocumentationIds
{
    /// <summary>Returns the identifier of a type, such as <c>T:ViShap.Rewind.RewindCell`1</c>.</summary>
    public static string Of(Type type) => "T:" + TypeId(type);

    /// <summary>Returns the identifier of a field or constant, such as <c>F:ViShap.Rewind.RewindDiagnostics.MeterName</c>.</summary>
    public static string Of(FieldInfo field) => $"F:{TypeId(field.DeclaringType!)}.{field.Name}";

    /// <summary>Returns the identifier of a property, such as <c>P:ViShap.Rewind.RewindCell`1.Value</c>.</summary>
    public static string Of(PropertyInfo property)
    {
        var parameters = property.GetIndexParameters();
        var id = $"P:{TypeId(property.DeclaringType!)}.{property.Name}";
        return parameters.Length == 0 ? id : $"{id}({string.Join(",", parameters.Select(parameter => ParameterId(parameter.ParameterType)))})";
    }

    /// <summary>Returns the identifier of a method or constructor, such as <c>M:ViShap.Rewind.Rewind.Run``1(``0)</c>.</summary>
    public static string Of(MethodBase method)
    {
        var id = new StringBuilder("M:").Append(TypeId(method.DeclaringType!)).Append('.');
        id.Append(method is ConstructorInfo ? "#ctor" : method.Name);
        if (method.IsGenericMethodDefinition)
        {
            id.Append("``").Append(method.GetGenericArguments().Length);
        }

        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            id.Append('(').AppendJoin(",", parameters.Select(parameter => ParameterId(parameter.ParameterType))).Append(')');
        }

        return id.ToString();
    }

    private static string TypeId(Type type) =>
        type.DeclaringType is { } outer ? $"{TypeId(outer)}.{type.Name}" : $"{type.Namespace}.{type.Name}";

    private static string ParameterId(Type type)
    {
        if (type.IsByRef)
        {
            return ParameterId(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            return ParameterId(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericParameter)
        {
            return type.DeclaringMethod is null ? "`" + type.GenericParameterPosition : "``" + type.GenericParameterPosition;
        }

        if (type.IsGenericType)
        {
            var name = TypeId(type.GetGenericTypeDefinition());
            name = name[..name.IndexOf('`')];
            return $"{name}{{{string.Join(",", type.GetGenericArguments().Select(ParameterId))}}}";
        }

        if (type.IsGenericParameter)
        {
            return "`" + type.GenericParameterPosition;
        }

        return TypeId(type);
    }
}
