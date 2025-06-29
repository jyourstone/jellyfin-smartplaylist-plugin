using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    // This is taken entirely from https://stackoverflow.com/questions/6488034/how-to-implement-a-rule-engine
    public static class Engine
    {
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
                        var equalsMethod = typeof(string).GetMethod("Equals", new[] { typeof(string), typeof(StringComparison) });
                        return System.Linq.Expressions.Expression.Call(left, equalsMethod, right, comparison);
                    case "NotEqual":
                        var notEqualsMethod = typeof(string).GetMethod("Equals", new[] { typeof(string), typeof(StringComparison) });
                        var equalsCall = System.Linq.Expressions.Expression.Call(left, notEqualsMethod, right, comparison);
                        return System.Linq.Expressions.Expression.Not(equalsCall);
                    case "Contains":
                        var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string), typeof(StringComparison) });
                        return System.Linq.Expressions.Expression.Call(left, containsMethod, right, comparison);
                    case "NotContains":
                        var notContainsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string), typeof(StringComparison) });
                        var containsCall = System.Linq.Expressions.Expression.Call(left, notContainsMethod, right, comparison);
                        return System.Linq.Expressions.Expression.Not(containsCall);
                }
            }

            if (tProp == typeof(bool) && r.Operator == "Equal")
            {
                var right = System.Linq.Expressions.Expression.Constant(bool.Parse(r.TargetValue));
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
                var regex = new Regex(r.TargetValue, RegexOptions.None);
                var method = typeof(Regex).GetMethod("IsMatch", new[] { typeof(string) });
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
                logger?.LogDebug("SmartPlaylist field {Field} is not IEnumerable, proceeding to fallback", r.MemberName);
            }

            // Fallback for other methods - this is still a bit fragile
            // but will work for simple cases.
            logger?.LogDebug("SmartPlaylist falling back to method: {Operator} on type {Type}", r.Operator, tProp.Name);
            var fallbackMethod = tProp.GetMethod(r.Operator);
            if (fallbackMethod == null)
            {
                logger?.LogError("SmartPlaylist method {Operator} not found on type {Type}", r.Operator, tProp.Name);
                throw new InvalidOperationException($"Method {r.Operator} not found on type {tProp.Name}");
            }
            var fallbackConvertedRight = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(r.TargetValue, fallbackMethod.GetParameters()[0].ParameterType));
            return System.Linq.Expressions.Expression.Call(left, fallbackMethod, fallbackConvertedRight);
        }

        public static Func<T, bool> CompileRule<T>(Expression r, ILogger logger = null)
        {
            var paramUser = System.Linq.Expressions.Expression.Parameter(typeof(T));
            var expr = BuildExpr<T>(r, paramUser, logger);
            return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(expr, paramUser).Compile();
        }

        private static bool AnyItemContains(IEnumerable<string> list, string value)
        {
            if (list == null) return false;
            return list.Any(s => s != null && s.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool AnyRegexMatch(IEnumerable<string> list, string pattern)
        {
            if (list == null) return false;
            
            try
            {
                var regex = new Regex(pattern, RegexOptions.None);
                return list.Any(s => s != null && regex.IsMatch(s));
            }
            catch (Exception)
            {
                // If regex pattern is invalid, fall back to basic string contains
                return list.Any(s => s != null && s.Contains(pattern, StringComparison.OrdinalIgnoreCase));
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