using System;
using System.Text.RegularExpressions;
using Trino.Client.Utils;

namespace Trino.Client
{
    /// <summary>
    /// Supports Trino ClientSelectedRole header.
    /// </summary>
    public class ClientSelectedRole
    {
        public enum Type
        {
            ROLE, ALL, NONE
        }

        public Type RoleType { get; }
        public string Role { get; }

        public ClientSelectedRole(Type roleType, string role)
        {
            this.RoleType = roleType.IsNullArgument("roletype");
            this.Role = role.IsNullArgument("role");
        }

        public override bool Equals(Object o)
        {
            if (this == o)
            {
                return true;
            }
            if (o == null || this.GetType() != o.GetType())
            {
                return false;
            }
            ClientSelectedRole that = (ClientSelectedRole)o;
            return RoleType == that.RoleType &&
                    Role == this.Role;
        }

        public override int GetHashCode()
        {
            return RoleType.GetHashCode() ^ Role.GetHashCode();
        }

        public ClientSelectedRole Clone()
        {
            return new ClientSelectedRole(this.RoleType, this.Role);
        }

        /// <summary>
        /// Serializes the role to the Trino wire format: "ALL", "NONE", or "ROLE{roleName}".
        /// </summary>
        public override string ToString()
        {
            if (RoleType == Type.ALL) return "ALL";
            if (RoleType == Type.NONE) return "NONE";
            if (RoleType == Type.ROLE) return "ROLE{" + Role + "}";
            throw new InvalidOperationException("Unexpected role type: " + RoleType);
        }

        /// <summary>
        /// Parses a role from the Trino wire format: "ALL", "NONE", or "ROLE{roleName}".
        /// </summary>
        public static ClientSelectedRole Parse(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                return new ClientSelectedRole(Type.ALL, string.Empty);
            if (value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return new ClientSelectedRole(Type.NONE, string.Empty);
            if (value.StartsWith("ROLE{", StringComparison.OrdinalIgnoreCase) && value.EndsWith("}"))
                return new ClientSelectedRole(Type.ROLE, value.Substring(5, value.Length - 6));
            throw new ArgumentException($"Cannot parse role value: '{value}'");
        }
    }
}
