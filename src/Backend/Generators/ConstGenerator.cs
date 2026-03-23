using System.Text;
using Reinforced.Typings;
using Reinforced.Typings.Ast;
using Reinforced.Typings.Generators;

namespace SiteChecker.Backend.Generators;

public class ConstGenerator : ClassCodeGenerator
{
    public override RtClass? GenerateNode(Type element, RtClass result, TypeResolver resolver)
    {
        var node = base.GenerateNode(element, result, resolver);

        var sb = new StringBuilder();
        sb.AppendLine($"export const {node.Name} = {{");

        foreach (var field in node.Members.OfType<RtField>())
        {
            var value = field.InitializationExpression.Replace("`", "'");
            sb.AppendLine($"\t{field.Identifier}: {value},");
        }

        sb.AppendLine("} as const;");

        Context.Location.CurrentNamespace?.CompilationUnits.Add(new RtRaw(sb.ToString()));
        return null;
    }
}
