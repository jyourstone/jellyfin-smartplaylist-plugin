using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    // This is based on https://stackoverflow.com/questions/6488034/how-to-implement-a-rule-engine
    public static class Engine
    {
        // Cache for compiled regex patterns to avoid recompilation
        private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();
        
        /// <summary>
        /// Gets or creates a compiled regex pattern from the cache.
        /// </summary>
        /// <param name="pattern">The regex pattern</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <returns>The compiled regex</returns>
        /// <exception cref="ArgumentException">Thrown when the regex pattern is invalid</exception>
        private static Regex GetOrCreateRegex(string pattern, ILogger logger = null)
        {
            return _regexCache.GetOrAdd(pattern, key =>
            {
                try
                {
                    logger?.LogDebug("SmartPlaylist compiling new regex pattern: {Pattern}", key);
                    return new Regex(key, RegexOptions.Compiled | RegexOptions.None);
                }
                catch (ArgumentException ex)
                {
                    logger?.LogError(ex, "Invalid regex pattern '{Pattern}': {Message}", key, ex.Message);
                    throw new ArgumentException($"Invalid regex pattern '{key}': {ex.Message}");
                }
            });
        }
        
        private static System.Linq.Expressions.Expression BuildExpr<T>(Expression r, ParameterExpression param, ILogger logger = null)
        {
            var left = System.Linq.Expressions.Expression.PropertyOrField(param, r.MemberName);
            var tProp = left.Type;
            
            logger?.LogDebug("SmartPlaylist BuildExpr: Field={Field}, Type={Type}, Operator={Operator}", r.MemberName, tProp.Name, r.Operator);

            if (tProp == typeof(string))
            {
                var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                var comparison = System.Linq.Expressions.Expression.Constant(StringComparison.OrdinalIgnoreCase);

                switch (r.Operator)
                {
                    case "Equal":
                        var equalsMethod = typeof(string).GetMethod("Equals", [typeof(string), typeof(StringComparison)]);
                        return System.Linq.Expressions.Expression.Call(left, equalsMethod, right, comparison);
                    case "NotEqual":
                        var notEqualsMethod = typeof(string).GetMethod("Equals", [typeof(string), typeof(StringComparison)]);
                        var equalsCall = System.Linq.Expressions.Expression.Call(left, notEqualsMethod, right, comparison);
                        return System.Linq.Expressions.Expression.Not(equalsCall);
                    case "Contains":
                        var containsMethod = typeof(string).GetMethod("Contains", [typeof(string), typeof(StringComparison)]);
                        return System.Linq.Expressions.Expression.Call(left, containsMethod, right, comparison);
                    case "NotContains":
                        var notContainsMethod = typeof(string).GetMethod("Contains", [typeof(string), typeof(StringComparison)]);
                        var containsCall = System.Linq.Expressions.Expression.Call(left, notContainsMethod, right, comparison);
                        return System.Linq.Expressions.Expression.Not(containsCall);
                }
            }

            if (tProp == typeof(bool) && r.Operator == "Equal")
            {
                // Validate and parse boolean value safely
                if (string.IsNullOrWhiteSpace(r.TargetValue))
                {
                    logger?.LogError("SmartPlaylist boolean comparison failed: TargetValue is null or empty for field '{Field}'", r.MemberName);
                    throw new ArgumentException($"Boolean comparison requires a valid true/false value for field '{r.MemberName}', but got: '{r.TargetValue}'");
                }
                
                if (!bool.TryParse(r.TargetValue, out bool boolValue))
                {
                    logger?.LogError("SmartPlaylist boolean comparison failed: Invalid boolean value '{Value}' for field '{Field}'", r.TargetValue, r.MemberName);
                    throw new ArgumentException($"Invalid boolean value '{r.TargetValue}' for field '{r.MemberName}'. Expected 'true' or 'false'.");
                }
                
                var right = System.Linq.Expressions.Expression.Constant(boolValue);
                return System.Linq.Expressions.Expression.MakeBinary(ExpressionType.Equal, left, right);
            }

            // is the operator a known .NET operator?
            logger?.LogDebug("SmartPlaylist checking if {Operator} is a valid ExpressionType", r.Operator);
            if (Enum.TryParse(r.Operator, out ExpressionType tBinary))
            {
                logger?.LogDebug("SmartPlaylist {Operator} IS a valid ExpressionType: {ExpressionType}", r.Operator, tBinary);
                var right = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(r.TargetValue, tProp));
                // use a binary operation, e.g. 'Equal' -> 'u.Age == 15'
                return System.Linq.Expressions.Expression.MakeBinary(tBinary, left, right);
            }
            logger?.LogDebug("SmartPlaylist {Operator} is NOT a valid ExpressionType, continuing", r.Operator);

            if (r.Operator == "MatchRegex" && tProp == typeof(string))
            {
                logger?.LogDebug("SmartPlaylist applying single string MatchRegex to {Field}", r.MemberName);
                var regex = GetOrCreateRegex(r.TargetValue, logger);
                var method = typeof(Regex).GetMethod("IsMatch", [typeof(string)]);
                var regexConstant = System.Linq.Expressions.Expression.Constant(regex);
                return System.Linq.Expressions.Expression.Call(regexConstant, method, left);
            }

            // Handle Contains for IEnumerable
            var ienumerable = tProp.GetInterface("IEnumerable`1");
            logger?.LogDebug("SmartPlaylist field {Field}: Type={Type}, IEnumerable={IsEnumerable}, Operator={Operator}", 
                r.MemberName, tProp.Name, ienumerable != null, r.Operator);
                
            if (ienumerable != null)
            {
                if (ienumerable.GetGenericArguments()[0] == typeof(string))
                {
                    if (r.Operator == "Contains" || r.Operator == "NotContains")
                    {
                        var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                        var method = typeof(Engine).GetMethod("AnyItemContains", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        var containsCall = System.Linq.Expressions.Expression.Call(method, left, right);
                        if (r.Operator == "Contains") return containsCall;
                        if (r.Operator == "NotContains") return System.Linq.Expressions.Expression.Not(containsCall);
                    }
                    if (r.Operator == "MatchRegex")
                    {
                        var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                        var method = typeof(Engine).GetMethod("AnyRegexMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (method == null)
                        {
                            logger?.LogError("SmartPlaylist AnyRegexMatch method not found!");
                            throw new InvalidOperationException("AnyRegexMatch method not found");
                        }
                        logger?.LogDebug("SmartPlaylist building regex expression for field: {Field}, pattern: {Pattern}", r.MemberName, r.TargetValue);
                        return System.Linq.Expressions.Expression.Call(method, left, right);
                    }
                }
                else // For other IEnumerable types, use the default Contains
                {
                    if (r.Operator == "Contains" || r.Operator == "NotContains")
                    {
                        var genericType = ienumerable.GetGenericArguments()[0];
                        var convertedRight = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(r.TargetValue, genericType));
                        var method = typeof(Enumerable).GetMethods()
                            .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                            .MakeGenericMethod(genericType);
                        
                        var call = System.Linq.Expressions.Expression.Call(method, left, convertedRight);
                        if (r.Operator == "Contains") return call;
                        if (r.Operator == "NotContains") return System.Linq.Expressions.Expression.Not(call);
                    }
                }
            }
            else 
            {
                logger?.LogDebug("SmartPlaylist field {Field} is not IEnumerable", r.MemberName);
            }

            // All supported operators have been handled explicitly above
            // If we reach here, the operator is not supported for this field type
            logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for field '{Field}' of type '{Type}'", r.Operator, r.MemberName, tProp.Name);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for field '{r.MemberName}' of type '{tProp.Name}'. Supported operators depend on the field type.");
        }

        public static Func<T, bool> CompileRule<T>(Expression r, ILogger logger = null)
        {
            var paramUser = System.Linq.Expressions.Expression.Parameter(typeof(T));
            var expr = BuildExpr<T>(r, paramUser, logger);
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(expr, paramUser).Compile();
        }

        internal static bool AnyItemContains(IEnumerable<string> list, string value)
        {
            if (list == null) return false;
            return list.Any(s => s != null && s.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool AnyRegexMatch(IEnumerable<string> list, string pattern)
        {
            if (list == null) return false;
            
            try
            {
                var regex = GetOrCreateRegex(pattern);
                return list.Any(s => s != null && regex.IsMatch(s));
            }
            catch (ArgumentException ex)
            {
                // Preserve the original error details while providing context
                throw new ArgumentException($"Invalid regex pattern '{pattern}': {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // For other unexpected errors, preserve the original exception details
                throw new ArgumentException($"Regex pattern '{pattern}' caused an error: {ex.Message}", ex);
            }
        }

        public static List<ExpressionSet> FixRuleSets(List<ExpressionSet> rulesets)
        {
            return rulesets;
        }

        public static ExpressionSet FixRules(ExpressionSet rules)
        {
            return rules;
        }
    }
}