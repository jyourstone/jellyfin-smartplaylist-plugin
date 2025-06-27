using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Linq;

namespace Jellyfin.Plugin.SmartPlaylist.QueryEngine
{
    // This is taken entirely from https://stackoverflow.com/questions/6488034/how-to-implement-a-rule-engine
    public class Engine
    {
        private static System.Linq.Expressions.Expression BuildExpr<T>(Expression r, ParameterExpression param)
        {
            var left = System.Linq.Expressions.Expression.Property(param, r.MemberName);
            var tProp = left.Type;

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
            if (Enum.TryParse(r.Operator, out ExpressionType tBinary))
            {
                var right = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(r.TargetValue, tProp));
                // use a binary operation, e.g. 'Equal' -> 'u.Age == 15'
                return System.Linq.Expressions.Expression.MakeBinary(tBinary, left, right);
            }

            if (r.Operator == "MatchRegex" || r.Operator == "NotMatchRegex")
            {
                var regex = new Regex(r.TargetValue);
                var method = typeof(Regex).GetMethod("IsMatch", new[] { typeof(string) });
                Debug.Assert(method != null, nameof(method) + " != null");
                var callInstance = System.Linq.Expressions.Expression.Constant(regex);

                var toStringMethod = tProp.GetMethod("ToString", new Type[0]);
                Debug.Assert(toStringMethod != null, nameof(toStringMethod) + " != null");
                var methodParam = System.Linq.Expressions.Expression.Call(left, toStringMethod);

                var call = System.Linq.Expressions.Expression.Call(callInstance, method, methodParam);
                if (r.Operator == "MatchRegex") return call;
            }

            // Handle Contains for IEnumerable
            var ienumerable = tProp.GetInterface("IEnumerable`1");
            if (ienumerable != null)
            {
                if (r.Operator == "Contains" || r.Operator == "NotContains")
                {
                    if (ienumerable.GetGenericArguments()[0] == typeof(string))
                    {
                        var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                        var method = typeof(Engine).GetMethod("AnyItemContains", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        var containsCall = System.Linq.Expressions.Expression.Call(method, left, right);
                        if (r.Operator == "Contains") return containsCall;
                        if (r.Operator == "NotContains") return System.Linq.Expressions.Expression.Not(containsCall);
                    }
                    else // For other IEnumerable types, use the default Contains
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
                if (r.Operator == "MatchRegex")
                {
                    var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                    var method = typeof(Engine).GetMethod("AnyRegexMatch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    return System.Linq.Expressions.Expression.Call(method, left, right);
                }
            }

            // Fallback for other methods - this is still a bit fragile
            // but will work for simple cases.
            var fallbackMethod = tProp.GetMethod(r.Operator);
            var fallbackConvertedRight = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(r.TargetValue, fallbackMethod.GetParameters()[0].ParameterType));
            return System.Linq.Expressions.Expression.Call(left, fallbackMethod, fallbackConvertedRight);
        }

        public static Func<T, bool> CompileRule<T>(Expression r)
        {
            var paramUser = System.Linq.Expressions.Expression.Parameter(typeof(T));
            var expr = BuildExpr<T>(r, paramUser);
            // build a lambda function User->bool and compile it
            var value = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(expr, paramUser).Compile(true);
            return value;
        }

        private static bool AnyItemContains(IEnumerable<string> list, string value)
        {
            if (list == null) return false;
            return list.Any(s => s != null && s.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        private static bool AnyRegexMatch(IEnumerable<string> list, string pattern)
        {
            if (list == null) return false;
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            return list.Any(s => s != null && regex.IsMatch(s));
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