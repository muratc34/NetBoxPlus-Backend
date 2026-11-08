using System.Collections.Frozen;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Auth.Domain.Roles;

/// <summary>
/// Every permission a role can hold, named <c>&lt;service&gt;:&lt;resource&gt;:&lt;action&gt;</c>.
/// A service checks the same string with <c>RequirePermission</c>; a new permission is added here
/// first, in the nested class of its service.
/// </summary>
public static partial class Permissions
{
    public static class Auth
    {
        public const string UsersRead = "auth:users:read";
        public const string UsersWrite = "auth:users:write";
        public const string RolesRead = "auth:roles:read";
        public const string RolesWrite = "auth:roles:write";

        /// <summary>Opening an admin session; without it an account cannot sign in to the admin panel.</summary>
        public const string AdminSignIn = "auth:admin:sign-in";
    }

    public static class Catalog
    {
        public const string MoviesRead = "catalog:movies:read";
        public const string MoviesWrite = "catalog:movies:write";
        public const string MoviesPublish = "catalog:movies:publish";
        public const string MoviesDelete = "catalog:movies:delete";
        public const string SeriesRead = "catalog:series:read";
        public const string SeriesWrite = "catalog:series:write";
        public const string SeriesPublish = "catalog:series:publish";
        public const string SeriesDelete = "catalog:series:delete";
        public const string PeopleRead = "catalog:people:read";
        public const string PeopleWrite = "catalog:people:write";
        public const string GenresRead = "catalog:genres:read";
        public const string GenresWrite = "catalog:genres:write";
    }

    /// <summary>Every constant declared above, collected once.</summary>
    public static IReadOnlySet<string> All { get; } = typeof(Permissions)
        .GetNestedTypes()
        .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToFrozenSet(StringComparer.Ordinal);

    public static bool IsKnown(string permission) => All.Contains(permission);

    public static bool IsWellFormed(string permission) => NamePattern().IsMatch(permission);

    [GeneratedRegex("^[a-z][a-z0-9-]*:[a-z][a-z0-9-]*:[a-z][a-z0-9-]*$")]
    private static partial Regex NamePattern();
}
