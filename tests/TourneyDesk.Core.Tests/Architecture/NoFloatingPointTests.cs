using System.Reflection;

namespace TourneyDesk.Core.Tests.Architecture;

/// <summary>
/// 需求：所有分數一律使用整數半分，不得用 double 或 float 表示分數或同分判定值。
/// 以反射掃描 Core 所有公開型別的欄位、屬性、方法參數與回傳值，任何浮點數或 decimal 都不允許。
/// </summary>
public class NoFloatingPointTests
{
    private static readonly Type[] Forbidden = [typeof(float), typeof(double), typeof(decimal)];

    private static bool IsForbidden(Type t) =>
        Forbidden.Contains(t) || (Nullable.GetUnderlyingType(t) is { } u && Forbidden.Contains(u))
        || (t.IsGenericType && t.GetGenericArguments().Any(IsForbidden))
        || (t.HasElementType && IsForbidden(t.GetElementType()!));

    [Fact]
    public void Core_public_api_has_no_floating_point_members()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offending = new List<string>();
        foreach (var type in Assembly.Load("TourneyDesk.Core").GetExportedTypes())
        {
            offending.AddRange(type.GetFields(flags).Where(f => IsForbidden(f.FieldType)).Select(f => $"{type.Name}.{f.Name}"));
            offending.AddRange(type.GetProperties(flags).Where(p => IsForbidden(p.PropertyType)).Select(p => $"{type.Name}.{p.Name}"));
            foreach (var m in type.GetMethods(flags).Concat<MethodBase>(type.GetConstructors()))
            {
                if (m is MethodInfo mi && IsForbidden(mi.ReturnType))
                    offending.Add($"{type.Name}.{m.Name}() 回傳");
                offending.AddRange(m.GetParameters().Where(p => IsForbidden(p.ParameterType)).Select(p => $"{type.Name}.{m.Name}({p.Name})"));
            }
        }

        Assert.Empty(offending);
    }
}
