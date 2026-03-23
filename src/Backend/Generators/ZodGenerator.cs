using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Reinforced.Typings;
using Reinforced.Typings.Ast;
using Reinforced.Typings.Generators;

namespace SiteChecker.Backend.Generators;

public static partial class TypeExtensions
{
    extension(Type type)
    {
        public bool IsSiteCheckerType() => type.Namespace?.StartsWith(nameof(SiteChecker)) == true;

        public bool TryGetSiteCheckerBaseType([NotNullWhen(true)] out Type? baseType)
        {
            baseType = type.BaseType;
            if (baseType?.IsSiteCheckerType() == true)
            {
                return true;
            }

            baseType = type.GetInterfaces()
                .FirstOrDefault(i => i.IsSiteCheckerType());
            return baseType != null;
        }

        public string GetTypeName()
        {
            var regex = new Regex(@"<.*>$");
            var typeName = type.ShortDisplayName();
            return regex.Replace(typeName, string.Empty);
        }
    }
}

public static class PropertyInfoExtensions
{
    extension(PropertyInfo propInfo)
    {
        public bool IsNullable()
        {
            var nullInfo = new NullabilityInfoContext().Create(propInfo);
            return nullInfo.WriteState is NullabilityState.Nullable;
        }
    }
}

public class ZodEnumGenerator : EnumGenerator
{
    public override RtEnum? GenerateNode(Type element, RtEnum result, TypeResolver resolver)
    {
        if (Context.Location.CurrentNamespace == null)
        {
            return null;
        }

        var node = base.GenerateNode(element, result, resolver);

        var sb = new StringBuilder();
        sb.AppendLine($"export const {node.EnumName} = z.enum([");
        foreach (var value in node.Values)
        {
            sb.AppendLine($"\t{value.EnumValue},");
        }
        sb.AppendLine("]);");

        var rtRaw = new RtRaw(sb.ToString());
        Context.Location.CurrentNamespace.CompilationUnits.Add(rtRaw);

        return null;
    }
}

public class ZodGenerator : ClassAndInterfaceGeneratorBase<RtRaw>
{
    private const string ItemSchemaParam = "itemSchema";

    private static readonly Dictionary<string, string> TypeMappings = new()
    {
        { "string", "z.string()" },
        { "number", "z.number()" },
        { "boolean", "z.boolean()" },
        { "T", ItemSchemaParam },
    };

    private static string GetZodType(Type type, TypeResolver resolver)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
        {
            return GetZodType(nullableType, resolver);
        }

        if (type.IsSiteCheckerType())
        {
            var typeName = type.GetTypeName();
            if (!string.IsNullOrEmpty(typeName))
            {
                return typeName;
            }
        }

        var resolvedTypeName = resolver.ResolveTypeName(type).ToString() ?? "any";
        if (TypeMappings.TryGetValue(resolvedTypeName, out var mappedType))
        {
            return mappedType;
        }

        if (type.IsAssignableTo(typeof(IEnumerable)))
        {
            var arrayType = type.GenericTypeArguments.FirstOrDefault() ?? typeof(object);
            var innerType = GetZodType(arrayType, resolver);
            return $"z.array({innerType})";
        }

        return "z.unknown()";
    }

    private static IEnumerable<PropertyInfo> GetTypeProperties(Type type)
    {
        var interfaceProps = type
            .GetInterfaces()
            .SelectMany(i => i.GetProperties())
            .Select(p => p.Name)
            .ToHashSet();

        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var properties = type.GetProperties(flags)
            .Where(prop => prop.GetCustomAttribute<JsonIgnoreAttribute>() == null)
            .ToHashSet();

        return properties.Where(prop => !interfaceProps.Contains(prop.Name));
    }

    private static string GetZodSchema(Type element, TypeResolver resolver, int indentLevel = 0)
    {
        var indent = new string('\t', indentLevel);

        var sb = new StringBuilder();

        var objStart = "z.object";
        if (element.TryGetSiteCheckerBaseType(out var baseType))
        {
            var baseObjName = baseType.GetTypeName();
            objStart = $"{baseObjName}.extend";
        }

        sb.AppendLine($"{objStart}({{");
        // sb.AppendLine($"{indent}\t// Resolved:   {resolver.ResolveTypeName(element)}");
        // sb.AppendLine($"{indent}\t// FullName:   {element.FullName}");
        // sb.AppendLine($"{indent}\t// Parent:     {element.BaseType?.FullName}");
        // sb.AppendLine($"{indent}\t// Interfaces: {string.Join(", ", element.GetInterfaces().Select(i => i.FullName))}");

        foreach (var prop in GetTypeProperties(element))
        {
            var propName = prop.Name[0].ToString().ToLowerInvariant() + prop.Name[1..];

            var zodType = GetZodType(prop.PropertyType, resolver);
            if (zodType.EndsWith("") && prop.IsNullable())
            {
                zodType += ".nullable()";
            }

            // sb.AppendLine();
            // sb.AppendLine($"{indent}\t// ResolveTypeName: {resolver.ResolveTypeName(prop.PropertyType)}");
            // sb.AppendLine($"{indent}\t// FullName: {prop.PropertyType.FullName}");
            sb.AppendLine($"{indent}\t{propName}: {zodType},");
        }

        sb.Append($"{indent}}});");
        return sb.ToString();
    }

    public override RtRaw GenerateNode(Type element, RtRaw node, TypeResolver resolver)
    {
        Console.WriteLine($"Generating Zod schema for {element.Name}");

        var objName = element.GetTypeName();

        var sb = new StringBuilder();

        if (element.IsGenericType)
        {
            var zodSchema = GetZodSchema(element, resolver, 1);
            sb.AppendLine($"export function {objName}<T extends z.ZodType>({ItemSchemaParam}: T) {{");
            sb.AppendLine($"\treturn {zodSchema}");
            sb.AppendLine("}");
            sb.AppendLine($"export type {objName}<T extends z.ZodType> = z.infer<ReturnType<typeof {objName}<T>>>;");
        }
        else
        {
            var zodSchema = GetZodSchema(element, resolver);
            sb.AppendLine($"export const {objName} = {zodSchema}");
            sb.AppendLine($"export type {objName} = z.infer<typeof {objName}>;");
        }

        return new RtRaw(sb.ToString());
    }
}
