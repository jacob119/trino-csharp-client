using Trino.Client.Model.StatementV1;
using Trino.Client.Types;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Trino.Client.Utils
{
    /// <summary>
    /// Contains all type converters for Trino types.
    /// Aligned with Trino 478 ClientStandardTypes.java and JsonDecodingUtils.java.
    /// </summary>
    public class TrinoTypeConverters
    {
        // Numeric types
        public const string TRINO_BIGINT = "bigint";
        public const string TRINO_INTEGER = "integer";
        public const string TRINO_SMALLINT = "smallint";
        public const string TRINO_TINYINT = "tinyint";
        public const string TRINO_DOUBLE = "double";
        public const string TRINO_REAL = "real";
        public const string TRINO_DECIMAL = "decimal";
        public const string TRINO_NUMBER = "number";

        // Boolean
        public const string TRINO_BOOLEAN = "boolean";

        // Date/Time types
        public const string TRINO_DATE = "date";
        public const string TRINO_TIME = "time";
        public const string TRINO_WITH_TIME_ZONE_SUFFIX = "with time zone";
        public const string TRINO_TIME_WITH_TIME_ZONE = "time " + TRINO_WITH_TIME_ZONE_SUFFIX;
        public const string TRINO_TIMESTAMP = "timestamp";
        public const string TRINO_TIMESTAMP_WITH_TIME_ZONE = "timestamp " + TRINO_WITH_TIME_ZONE_SUFFIX;
        public const string TRINO_INTERVAL_YEAR_TO_MONTH = "interval year to month";
        public const string TRINO_INTERVAL_DAY_TO_SECOND = "interval day to second";

        // String/Text types
        public const string TRINO_VARCHAR = "varchar";
        public const string TRINO_CHAR = "char";

        // Binary types
        public const string TRINO_VARBINARY = "varbinary";

        // Structural types
        public const string TRINO_ARRAY = "array";
        public const string TRINO_MAP = "map";
        public const string TRINO_ROW = "row";

        // JSON/Variant types
        public const string TRINO_JSON = "json";
        public const string TRINO_JSON_2016 = "json2016";
        public const string TRINO_VARIANT = "variant";

        // Network types
        public const string TRINO_IP = "ipaddress";
        public const string TRINO_UUID = "uuid";

        // Geo types (returned as string, like Java StringDecoder)
        public const string TRINO_GEOMETRY = "Geometry";
        public const string TRINO_SPHERICAL_GEOGRAPHY = "SphericalGeography";

        // Sketch/analytics types (returned as byte[], like Java Base64Decoder)
        public const string TRINO_HYPER_LOG_LOG = "HyperLogLog";
        public const string TRINO_P4_HYPER_LOG_LOG = "P4HyperLogLog";
        public const string TRINO_SET_DIGEST = "SetDigest";
        public const string TRINO_QDIGEST = "qdigest";
        public const string TRINO_TDIGEST = "tdigest";

        // Tile/spatial types (returned as string)
        public const string TRINO_BING_TILE = "BingTile";
        public const string TRINO_KDB_TREE = "KdbTree";

        // Misc
        public const string TRINO_COLOR = "color";
        public const string TRINO_UNKNOWN = "unknown";

        private static readonly string TrinoDateFormat = "yyyy-MM-dd";
        private static readonly Regex TrinoTimestampWithTimezoneFormat = new Regex(@"([\d]{4})-([\d]{2})-([\d]{2}) ([\d]{2})\:([\d]{2})\:([\d]{2})(\.([\d]+))? ([+-]\d\d:\d\d|UTC)");
        private static readonly Regex TrinoOffset = new Regex(@"([+-]\d\d):(\d\d)");
        private static readonly string TrinoTimeFormat = "hh\\:mm\\:ss\\.fff";

        internal static object ConvertToTrinoTypeFromJson(object value, string trinoType, string validType = null)
        {
            if (value == null)
            {
                return null;
            }

            GetNestedTypes(trinoType, out string baseType, out string typeParameters);

            if (!string.IsNullOrEmpty(validType) && validType != baseType)
            {
                throw new ArgumentException($"Column is type {trinoType} but a value of type {validType} was expected");
            }

            return ConvertToTrinoTypeFromJson(value, trinoType, baseType, typeParameters);
        }

        internal static object ConvertToTrinoTypeFromJson(object value, string trinoType, string[] validTypes)
        {
            if (value == null)
            {
                return null;
            }

            GetNestedTypes(trinoType, out string baseType, out string typeParameters);

            if (!validTypes.Contains(baseType))
            {
                throw new ArgumentException($"Column is type {trinoType} but a value of type {string.Join(", ", validTypes)} was expected");
            }

            return ConvertToTrinoTypeFromJson(value, trinoType, baseType, typeParameters);
        }

        private static object ConvertToTrinoTypeFromJson(object value, string trinoType, string baseType, string typeParameters)
        {
            switch (baseType)
            {
                case TRINO_BIGINT:
                    return value;
                case TRINO_INTEGER:
                    return Convert.ToInt32(value);
                case TRINO_SMALLINT:
                    return Convert.ToInt16(value);
                case TRINO_TINYINT:
                    return Convert.ToSByte(value);
                case TRINO_BOOLEAN:
                    return value;
                case TRINO_DOUBLE:
                    return value;
                case TRINO_REAL:
                    return Convert.ToSingle(value);
                case TRINO_DECIMAL:
                    return new TrinoBigDecimal(value.ToString());
                case TRINO_NUMBER:
                    return value.ToString();
                case TRINO_DATE:
                    return DateTime.ParseExact(value.ToString(), TrinoDateFormat, null, System.Globalization.DateTimeStyles.None);
                case TRINO_TIME:
                    return TimeSpan.ParseExact(value.ToString(), TrinoTimeFormat, null);
                case TRINO_TIME_WITH_TIME_ZONE:
                    return value.ToString();
                case TRINO_TIMESTAMP:
                    return DateTime.Parse(value.ToString());
                case TRINO_TIMESTAMP_WITH_TIME_ZONE:
                    return ParseTimestampWithTimezone(value.ToString());
                case TRINO_INTERVAL_YEAR_TO_MONTH:
                {
                    string raw = value.ToString();
                    int sep = raw.IndexOf('-');
                    return new TrinoIntervalYearToMonth(
                        Convert.ToInt32(raw.Substring(0, sep)),
                        Convert.ToInt32(raw.Substring(sep + 1)));
                }
                case TRINO_INTERVAL_DAY_TO_SECOND:
                    int dayToTimeSeparator = value.ToString().IndexOf(' ');
                    int days = Convert.ToInt32(value.ToString().Substring(0, dayToTimeSeparator));
                    string time = value.ToString().Substring(dayToTimeSeparator + 1);
                    return TimeSpan.ParseExact(time, TrinoTimeFormat, null).Add(TimeSpan.FromDays(days));
                case TRINO_VARCHAR:
                    return value.ToString();
                case TRINO_CHAR:
                    return value.ToString();
                case TRINO_JSON:
                case TRINO_JSON_2016:
                case TRINO_VARIANT:
                    return value.ToString();
                case TRINO_IP:
                    return value.ToString();
                case TRINO_UUID:
                    return Guid.Parse(value.ToString());
                case TRINO_VARBINARY:
                    return Convert.FromBase64String(value.ToString());
                // Sketch/analytics types — Base64-encoded binary (same as Java Base64Decoder)
                case "hyperloglog":
                case "p4hyperloglog":
                case "setdigest":
                case "qdigest":
                case "tdigest":
                case TRINO_COLOR:
                    return Convert.FromBase64String(value.ToString());
                // Geo types — returned as string (same as Java StringDecoder)
                case "geometry":
                case "sphericalgeography":
                    return value.ToString();
                // Tile types — returned as string (Java ObjectDecoder returns JObject; string is safest for netstandard2.0)
                case "bingtile":
                case "kdbtree":
                    return value.ToString();
                case TRINO_ARRAY:
                    return handleComplexType(baseType, typeParameters, value);
                case TRINO_MAP:
                    return handleComplexType(baseType, typeParameters, value);
                case TRINO_ROW:
                    return handleComplexType(baseType, typeParameters, value);
                case TRINO_UNKNOWN:
                    return null;
                default:
                    return value.ToString();
            }
        }

        private static DateTimeOffset ParseTimestampWithTimezone(string value)
        {
            Match timestampParts = TrinoTimestampWithTimezoneFormat.Match(value);
            if (!timestampParts.Success)
            {
                throw new TrinoException($"Could not parse timestamp with time zone: {value}");
            }

            int year = Convert.ToInt32(timestampParts.Groups[1].Value);
            int month = Convert.ToInt32(timestampParts.Groups[2].Value);
            int day = Convert.ToInt32(timestampParts.Groups[3].Value);
            int hour = Convert.ToInt32(timestampParts.Groups[4].Value);
            int minute = Convert.ToInt32(timestampParts.Groups[5].Value);
            int second = Convert.ToInt32(timestampParts.Groups[6].Value);

            // Trino supports up to 12 digits precision; DateTime/DateTimeOffset supports 7 (ticks = 100ns).
            // Truncate to 7 digits to avoid throwing on high-precision timestamps.
            string fractionStr = timestampParts.Groups[8].Value;
            if (fractionStr.Length > 7)
            {
                fractionStr = fractionStr.Substring(0, 7);
            }
            int fraction = string.IsNullOrEmpty(fractionStr) ? 0 : Convert.ToInt32(fractionStr);
            int fractionTicks = fraction * (int)Math.Pow(10, 7 - fractionStr.Length);

            string timezone = timestampParts.Groups[9].Value;
            TimeSpan offset = TimeSpan.Zero;
            if (timezone != "UTC")
            {
                Match offsetMatch = TrinoOffset.Match(timezone);
                if (!offsetMatch.Success)
                {
                    throw new TrinoException($"Could not parse timezone in timestamp: {value}");
                }
                int offsetHours = Convert.ToInt32(offsetMatch.Groups[1].Value);
                int offsetMinutes = Convert.ToInt32(offsetMatch.Groups[2].Value);
                offset = new TimeSpan(offsetHours, offsetMinutes, 0);
            }

            return new DateTimeOffset(year, month, day, hour, minute, second, offset).AddTicks(fractionTicks);
        }

        public static void GetNestedTypes(string trinoType, out string baseType, out string typeParameters)
        {
            int typeParametersIndex = trinoType.IndexOf("(");
            int typeParametersIndexEnd = trinoType.LastIndexOf(")");
            string raw = typeParametersIndex != -1 && typeParametersIndexEnd != -1
                ? trinoType.Substring(0, typeParametersIndex) + trinoType.Substring(typeParametersIndexEnd + 1, trinoType.Length - typeParametersIndexEnd - 1)
                : trinoType;
            // Normalize once here so switch statements don't need per-call ToLowerInvariant.
            baseType = raw.ToLowerInvariant().Trim();
            typeParameters = typeParametersIndex != -1 ? trinoType.Substring(typeParametersIndex + 1, typeParametersIndexEnd - typeParametersIndex - 1) : null;
        }

        public static Type GetClrTypeFromTrinoType(TrinoColumn trinoType)
        {
            GetNestedTypes(trinoType.type, out string baseType, out string typeParameters);

            switch (baseType)
            {
                case TRINO_BIGINT:
                    return typeof(long);
                case TRINO_INTEGER:
                    return typeof(int);
                case TRINO_SMALLINT:
                    return typeof(short);
                case TRINO_TINYINT:
                    return typeof(sbyte);
                case TRINO_BOOLEAN:
                    return typeof(bool);
                case TRINO_DOUBLE:
                    return typeof(double);
                case TRINO_REAL:
                    return typeof(float);
                case TRINO_DECIMAL:
                    return typeof(TrinoBigDecimal);
                case TRINO_NUMBER:
                    return typeof(string);
                case TRINO_DATE:
                    return typeof(DateTime);
                case TRINO_INTERVAL_YEAR_TO_MONTH:
                    return typeof(TrinoIntervalYearToMonth);
                case TRINO_TIME:
                case TRINO_INTERVAL_DAY_TO_SECOND:
                    return typeof(TimeSpan);
                case TRINO_TIME_WITH_TIME_ZONE:
                    return typeof(string);
                case TRINO_TIMESTAMP:
                    return typeof(DateTime);
                case TRINO_TIMESTAMP_WITH_TIME_ZONE:
                    return typeof(DateTimeOffset);
                case TRINO_VARCHAR:
                case TRINO_CHAR:
                case TRINO_JSON:
                case TRINO_JSON_2016:
                case TRINO_VARIANT:
                case TRINO_IP:
                    return typeof(string);
                case TRINO_UUID:
                    return typeof(Guid);
                case TRINO_VARBINARY:
                case "hyperloglog":
                case "p4hyperloglog":
                case "setdigest":
                case "qdigest":
                case "tdigest":
                case TRINO_COLOR:
                    return typeof(byte[]);
                case "geometry":
                case "sphericalgeography":
                case "bingtile":
                case "kdbtree":
                    return typeof(string);
                case TRINO_ARRAY:
                    return typeof(List<object>);
                case TRINO_MAP:
                    return typeof(Dictionary<object, object>);
                case TRINO_ROW:
                    return typeof(List<object>);
                default:
                    return typeof(string);
            }
        }

        private static object handleComplexType(string baseType, string typeParameters, object value)
        {
            if (baseType == TRINO_ARRAY)
            {
                if (typeParameters == null)
                {
                    throw new TrinoException("Array type must have type parameters");
                }

                if (value is string stringValue)
                {
                    try
                    {
                        value = JArray.Parse(stringValue);
                    }
                    catch (Exception ex)
                    {
                        throw new TrinoException("Failed to parse string as JSON array", ex);
                    }
                }

                if (value.GetType() != typeof(JArray))
                {
                    throw new TrinoException("Array type expected to be JArray");
                }

                List<object> list = new List<object>();
                foreach (var item in (JArray)value)
                {
                    list.Add(ConvertToTrinoTypeFromJson(item, typeParameters));
                }

                return list;
            }
            else if (baseType == TRINO_MAP)
            {
                if (typeParameters == null)
                {
                    throw new TrinoException("Map type must have type parameters");
                }
                if (value is string stringValue)
                {
                    try
                    {
                        value = JObject.Parse(stringValue);
                    }
                    catch (Exception ex)
                    {
                        throw new TrinoException("Failed to parse string as JObject", ex);
                    }
                }

                if (value.GetType() != typeof(JObject))
                {
                    throw new TrinoException("Map type expected to be JObject");
                }

                Dictionary<object, object> map = new Dictionary<object, object>();
                SplitTopLevelComma(typeParameters, out string keyType, out string valueType);
                foreach (KeyValuePair<string, JToken> item in (JObject)value)
                {
                    map.Add(ConvertToTrinoTypeFromJson(item.Key, keyType), ConvertToTrinoTypeFromJson(item.Value, valueType));
                }

                return map;
            }
            else if (baseType == TRINO_ROW)
            {
                // ROW arrives as a JSON array; field names are in column metadata (not available here).
                // Return as List<object> with raw values from the JSON array.
                if (value is string strVal)
                {
                    try
                    {
                        value = JArray.Parse(strVal);
                    }
                    catch (Exception ex)
                    {
                        throw new TrinoException("Failed to parse ROW value as JSON array", ex);
                    }
                }

                if (value.GetType() != typeof(JArray))
                {
                    throw new TrinoException("ROW type expected to be JArray");
                }

                List<object> row = new List<object>();
                foreach (var item in (JArray)value)
                {
                    row.Add(item?.ToString());
                }

                return row;
            }
            else
            {
                throw new TrinoException("Unknown complex type: " + baseType);
            }
        }

        /// <summary>
        /// Splits "keyType, valueType" at the first top-level comma,
        /// correctly handling nested types like "row(a integer, b varchar), varchar".
        /// </summary>
        private static void SplitTopLevelComma(string typeParameters, out string first, out string second)
        {
            int depth = 0;
            for (int i = 0; i < typeParameters.Length; i++)
            {
                char c = typeParameters[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    first = typeParameters.Substring(0, i).Trim();
                    second = typeParameters.Substring(i + 1).Trim();
                    return;
                }
            }
            throw new TrinoException($"Map type parameters must have exactly two type arguments separated by a comma: {typeParameters}");
        }
    }
}
