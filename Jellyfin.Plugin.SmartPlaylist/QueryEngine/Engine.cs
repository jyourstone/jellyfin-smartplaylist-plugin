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
            // Handle user-specific expressions first
            if (r.IsUserSpecific)
            {
                if (r.UserSpecificField == null)
                {
                    logger?.LogError("SmartPlaylist user-specific expression for unsupported field '{Field}' with UserId '{UserId}'", r.MemberName, r.UserId);
                    throw new ArgumentException($"Field '{r.MemberName}' does not support user-specific queries. Supported user-specific fields are: IsPlayed, PlayCount, IsFavorite.");
                }
                return BuildUserSpecificExpression<T>(r, param, logger);
            }

            // Get the property/field expression
            var left = System.Linq.Expressions.Expression.PropertyOrField(param, r.MemberName);
            var tProp = left.Type;
            
            logger?.LogDebug("SmartPlaylist BuildExpr: Field={Field}, Type={Type}, Operator={Operator}", r.MemberName, tProp.Name, r.Operator);

            // Handle different field types with specialized handlers
            if (tProp == typeof(string))
            {
                return BuildStringExpression(r, left, logger);
            }
            
            if (tProp == typeof(bool))
            {
                return BuildBooleanExpression(r, left, logger);
            }
            
            if (tProp == typeof(double) && IsDateField(r.MemberName))
            {
                return BuildDateExpression(r, left, logger);
            }
            
            if (tProp.GetInterface("IEnumerable`1") != null)
            {
                return BuildEnumerableExpression(r, left, tProp, logger);
            }
            
            // Handle standard .NET operators for other types
            return BuildStandardOperatorExpression(r, left, tProp, logger);
        }

        /// <summary>
        /// Builds expressions for user-specific fields that require method calls.
        /// </summary>
        private static BinaryExpression BuildUserSpecificExpression<T>(Expression r, ParameterExpression param, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist BuildExpr: User-specific query for Field={Field}, UserId={UserId}, Operator={Operator}", r.MemberName, r.UserId, r.Operator);
            
            // Get the method to call (e.g., GetIsPlayedByUser)
            var methodName = r.UserSpecificField;
            var method = typeof(T).GetMethod(methodName, [typeof(string)]);
            
            if (method == null)
            {
                logger?.LogError("SmartPlaylist user-specific method '{Method}' not found for field '{Field}'", methodName, r.MemberName);
                throw new ArgumentException($"User-specific method '{methodName}' not found for field '{r.MemberName}'");
            }
            
            // Create the method call: operand.GetIsPlayedByUser(userId)
            var methodCall = System.Linq.Expressions.Expression.Call(param, method, System.Linq.Expressions.Expression.Constant(r.UserId));
            
            // Get the return type of the method to handle different data types properly
            var returnType = method.ReturnType;
            logger?.LogDebug("SmartPlaylist user-specific method '{Method}' returns type '{ReturnType}'", methodName, returnType.Name);
            
            // Handle different return types and operators appropriately
            if (returnType == typeof(bool))
            {
                return BuildUserSpecificBooleanExpression(r, methodCall, logger);
            }
            else if (returnType == typeof(int))
            {
                return BuildUserSpecificIntegerExpression(r, methodCall, logger);
            }
            else
            {
                logger?.LogError("SmartPlaylist unsupported return type '{ReturnType}' for user-specific method '{Method}'", returnType.Name, methodName);
                throw new ArgumentException($"User-specific method '{methodName}' returns unsupported type '{returnType.Name}' for field '{r.MemberName}'");
            }
        }

        /// <summary>
        /// Validates and parses a boolean TargetValue for expression building.
        /// </summary>
        /// <param name="targetValue">The target value to validate and parse</param>
        /// <param name="fieldName">The field name for error reporting</param>
        /// <param name="logger">Optional logger for error reporting</param>
        /// <returns>The parsed boolean value</returns>
        /// <exception cref="ArgumentException">Thrown when the target value is invalid</exception>
        private static bool ValidateAndParseBooleanValue(string targetValue, string fieldName, ILogger logger = null)
        {
            // Validate and parse boolean value safely
            if (string.IsNullOrWhiteSpace(targetValue))
            {
                logger?.LogError("SmartPlaylist boolean comparison failed: TargetValue is null or empty for field '{Field}'", fieldName);
                throw new ArgumentException($"Boolean comparison requires a valid true/false value for field '{fieldName}', but got: '{targetValue}'");
            }
            
            if (!bool.TryParse(targetValue, out bool boolValue))
            {
                logger?.LogError("SmartPlaylist boolean comparison failed: Invalid boolean value '{Value}' for field '{Field}'", targetValue, fieldName);
                throw new ArgumentException($"Invalid boolean value '{targetValue}' for field '{fieldName}'. Expected 'true' or 'false'.");
            }
            
            return boolValue;
        }

        /// <summary>
        /// Builds expressions for boolean user-specific fields.
        /// </summary>
        private static BinaryExpression BuildUserSpecificBooleanExpression(Expression r, System.Linq.Expressions.Expression methodCall, ILogger logger)
        {
            if (r.Operator != "Equal")
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for boolean user-specific field '{Field}'", r.Operator, r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for boolean user-specific field '{r.MemberName}'. Only 'Equal' is supported.");
            }
            
            var boolValue = ValidateAndParseBooleanValue(r.TargetValue, r.MemberName, logger);
            var right = System.Linq.Expressions.Expression.Constant(boolValue);
            return System.Linq.Expressions.Expression.MakeBinary(ExpressionType.Equal, methodCall, right);
        }

        /// <summary>
        /// Builds expressions for integer user-specific fields.
        /// </summary>
        private static BinaryExpression BuildUserSpecificIntegerExpression(Expression r, System.Linq.Expressions.Expression methodCall, ILogger logger)
        {
            if (string.IsNullOrWhiteSpace(r.TargetValue))
            {
                logger?.LogError("SmartPlaylist integer comparison failed: TargetValue is null or empty for field '{Field}'", r.MemberName);
                throw new ArgumentException($"Integer comparison requires a valid numeric value for field '{r.MemberName}', but got: '{r.TargetValue}'");
            }
            
            if (!int.TryParse(r.TargetValue, out int intValue))
            {
                logger?.LogError("SmartPlaylist integer comparison failed: Invalid integer value '{Value}' for field '{Field}'", r.TargetValue, r.MemberName);
                throw new ArgumentException($"Invalid integer value '{r.TargetValue}' for field '{r.MemberName}'. Expected a valid number.");
            }
            
            var right = System.Linq.Expressions.Expression.Constant(intValue);
            
            // Check if the operator is a known .NET operator for integer comparison
            if (Enum.TryParse(r.Operator, out ExpressionType intBinary))
            {
                logger?.LogDebug("SmartPlaylist {Operator} IS a built-in ExpressionType for integer field: {ExpressionType}", r.Operator, intBinary);
                return System.Linq.Expressions.Expression.MakeBinary(intBinary, methodCall, right);
            }
            else
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for integer user-specific field '{Field}'", r.Operator, r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for integer user-specific field '{r.MemberName}'. Supported operators: Equal, NotEqual, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual");
            }
        }

        /// <summary>
        /// Builds expressions for string fields.
        /// </summary>
        private static System.Linq.Expressions.Expression BuildStringExpression(Expression r, MemberExpression left, ILogger logger)
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
                case "MatchRegex":
                    logger?.LogDebug("SmartPlaylist applying single string MatchRegex to {Field}", r.MemberName);
                    var regex = GetOrCreateRegex(r.TargetValue, logger);
                    var method = typeof(Regex).GetMethod("IsMatch", [typeof(string)]);
                    var regexConstant = System.Linq.Expressions.Expression.Constant(regex);
                    return System.Linq.Expressions.Expression.Call(regexConstant, method, left);
                default:
                    logger?.LogError("SmartPlaylist unsupported string operator '{Operator}' for field '{Field}'", r.Operator, r.MemberName);
                    throw new ArgumentException($"Operator '{r.Operator}' is not supported for string field '{r.MemberName}'");
            }
        }

        /// <summary>
        /// Builds expressions for boolean fields.
        /// </summary>
        private static BinaryExpression BuildBooleanExpression(Expression r, MemberExpression left, ILogger logger)
        {
            if (r.Operator != "Equal")
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for boolean field '{Field}'", r.Operator, r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for boolean field '{r.MemberName}'. Only 'Equal' is supported.");
            }
            
            var boolValue = ValidateAndParseBooleanValue(r.TargetValue, r.MemberName, logger);
            var right = System.Linq.Expressions.Expression.Constant(boolValue);
            return System.Linq.Expressions.Expression.MakeBinary(ExpressionType.Equal, left, right);
        }

        /// <summary>
        /// Builds expressions for date fields (stored as Unix timestamps).
        /// </summary>
        private static BinaryExpression BuildDateExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling date field {Field} with value {Value}", r.MemberName, r.TargetValue);
            
            // Handle NewerThan and OlderThan operators first
            if (r.Operator == "NewerThan" || r.Operator == "OlderThan")
            {
                return BuildRelativeDateExpression(r, left, logger);
            }
            
            if (string.IsNullOrWhiteSpace(r.TargetValue))
            {
                logger?.LogError("SmartPlaylist date comparison failed: TargetValue is null or empty for field '{Field}'", r.MemberName);
                throw new ArgumentException($"Date comparison requires a valid date value for field '{r.MemberName}', but got: '{r.TargetValue}'");
            }
            
            // Convert date string to Unix timestamp
            double targetTimestamp;
            try
            {
                targetTimestamp = ConvertDateStringToUnixTimestamp(r.TargetValue);
                logger?.LogDebug("SmartPlaylist converted date '{DateString}' to Unix timestamp {Timestamp}", r.TargetValue, targetTimestamp);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "SmartPlaylist date conversion failed for field '{Field}' with value '{Value}'", r.MemberName, r.TargetValue);
                throw new ArgumentException($"Invalid date format '{r.TargetValue}' for field '{r.MemberName}'. Expected format: YYYY-MM-DD");
            }
            
            // Handle date equality specially - compare date ranges instead of exact timestamps
            if (r.Operator == "Equal")
            {
                return BuildDateEqualityExpression(r, left, logger);
            }
            else if (r.Operator == "NotEqual")
            {
                return BuildDateInequalityExpression(r, left, logger);
            }
            else if (r.Operator == "WithinLastDays")
            {
                return BuildWithinLastDaysExpression(r, left, logger);
            }
            else
            {
                // For other operators (GreaterThan, LessThan, etc.), use the exact timestamp comparison
                if (Enum.TryParse(r.Operator, out ExpressionType dateBinary))
                {
                    logger?.LogDebug("SmartPlaylist {Operator} IS a built-in ExpressionType for date field: {ExpressionType}", r.Operator, dateBinary);
                    var right = System.Linq.Expressions.Expression.Constant(targetTimestamp);
                    return System.Linq.Expressions.Expression.MakeBinary(dateBinary, left, right);
                }
                else
                {
                    logger?.LogError("SmartPlaylist unsupported date operator '{Operator}' for field '{Field}'", r.Operator, r.MemberName);
                    throw new ArgumentException($"Operator '{r.Operator}' is not supported for date field '{r.MemberName}'");
                }
            }
        }

        /// <summary>
        /// Builds expressions for relative date operators (NewerThan, OlderThan).
        /// </summary>
        private static BinaryExpression BuildRelativeDateExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling '{Operator}' for field {Field} with value {Value}", r.Operator, r.MemberName, r.TargetValue);
            
            // Parse value as number:unit
            var parts = (r.TargetValue ?? "").Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int num) || num <= 0)
            {
                logger?.LogError("SmartPlaylist '{Operator}' requires value in format number:unit, got: '{Value}'", r.Operator, r.TargetValue);
                throw new ArgumentException($"'{r.Operator}' requires value in format number:unit, but got: '{r.TargetValue}'");
            }
            
            string unit = parts[1].ToLowerInvariant();
            DateTimeOffset cutoffDate = unit switch
            {
                "days" => DateTimeOffset.UtcNow.AddDays(-num),
                "weeks" => DateTimeOffset.UtcNow.AddDays(-num * 7),
                "months" => DateTimeOffset.UtcNow.AddMonths(-num),
                "years" => DateTimeOffset.UtcNow.AddYears(-num),
                _ => throw new ArgumentException($"Unknown unit '{unit}' for '{r.Operator}'")
            };
            
            var cutoffTimestamp = (double)cutoffDate.ToUnixTimeSeconds();
            logger?.LogDebug("SmartPlaylist '{Operator}' cutoff: {CutoffDate} (timestamp: {Timestamp})", r.Operator, cutoffDate, cutoffTimestamp);
            var cutoffConstant = System.Linq.Expressions.Expression.Constant(cutoffTimestamp);
            
            if (r.Operator == "NewerThan")
            {
                // operand.DateCreated >= cutoffTimestamp
                return System.Linq.Expressions.Expression.GreaterThanOrEqual(left, cutoffConstant);
            }
            else
            {
                // operand.DateCreated < cutoffTimestamp
                return System.Linq.Expressions.Expression.LessThan(left, cutoffConstant);
            }
        }

        /// <summary>
        /// Builds expressions for date equality (comparing date ranges).
        /// </summary>
        private static BinaryExpression BuildDateEqualityExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling date equality for field {Field} with date {Date}", r.MemberName, r.TargetValue);
            
            // For equality, we need to check if the date falls within the target day
            // Convert the target date to start and end of day timestamps using UTC
            var targetDate = DateTime.ParseExact(r.TargetValue, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var startOfDay = (double)new DateTimeOffset(targetDate, TimeSpan.Zero).ToUnixTimeSeconds();
            var endOfDay = (double)new DateTimeOffset(targetDate.AddDays(1).AddSeconds(-1), TimeSpan.Zero).ToUnixTimeSeconds();
            
            logger?.LogDebug("SmartPlaylist date equality range: {StartOfDay} to {EndOfDay}", startOfDay, endOfDay);
            
            // Create expression: operand.DateCreated >= startOfDay && operand.DateCreated <= endOfDay
            var startConstant = System.Linq.Expressions.Expression.Constant(startOfDay);
            var endConstant = System.Linq.Expressions.Expression.Constant(endOfDay);
            
            var greaterThanOrEqual = System.Linq.Expressions.Expression.GreaterThanOrEqual(left, startConstant);
            var lessThanOrEqual = System.Linq.Expressions.Expression.LessThanOrEqual(left, endConstant);
            
            return System.Linq.Expressions.Expression.AndAlso(greaterThanOrEqual, lessThanOrEqual);
        }

        /// <summary>
        /// Builds expressions for date inequality (outside date ranges).
        /// </summary>
        private static BinaryExpression BuildDateInequalityExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling date inequality for field {Field} with date {Date}", r.MemberName, r.TargetValue);
            
            // For inequality, we need to check if the date is outside the target day
            var targetDate = DateTime.ParseExact(r.TargetValue, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var startOfDay = (double)new DateTimeOffset(targetDate, TimeSpan.Zero).ToUnixTimeSeconds();
            var endOfDay = (double)new DateTimeOffset(targetDate.AddDays(1).AddSeconds(-1), TimeSpan.Zero).ToUnixTimeSeconds();
            
            logger?.LogDebug("SmartPlaylist date inequality range: < {StartOfDay} or > {EndOfDay}", startOfDay, endOfDay);
            
            // Create expression: operand.DateCreated < startOfDay || operand.DateCreated > endOfDay
            var startConstant = System.Linq.Expressions.Expression.Constant(startOfDay);
            var endConstant = System.Linq.Expressions.Expression.Constant(endOfDay);
            
            var lessThan = System.Linq.Expressions.Expression.LessThan(left, startConstant);
            var greaterThan = System.Linq.Expressions.Expression.GreaterThan(left, endConstant);
            
            return System.Linq.Expressions.Expression.OrElse(lessThan, greaterThan);
        }

        /// <summary>
        /// Builds expressions for WithinLastDays operator.
        /// </summary>
        private static BinaryExpression BuildWithinLastDaysExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling 'WithinLastDays' for field {Field} with {Days} days", r.MemberName, r.TargetValue);
            
            // Parse the number of days
            if (!int.TryParse(r.TargetValue, out int days) || days <= 0)
            {
                logger?.LogError("SmartPlaylist 'WithinLastDays' requires a positive integer for days, got: '{Value}'", r.TargetValue);
                throw new ArgumentException($"'WithinLastDays' requires a positive integer for days, but got: '{r.TargetValue}'");
            }
            
            // Calculate the cutoff date (X days ago from now)
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days);
            var cutoffTimestamp = (double)cutoffDate.ToUnixTimeSeconds();
            
            logger?.LogDebug("SmartPlaylist 'WithinLastDays' cutoff: {CutoffDate} (timestamp: {Timestamp})", cutoffDate, cutoffTimestamp);
            
            // Create expression: operand.DateCreated >= cutoffTimestamp
            var cutoffConstant = System.Linq.Expressions.Expression.Constant(cutoffTimestamp);
            return System.Linq.Expressions.Expression.GreaterThanOrEqual(left, cutoffConstant);
        }

        /// <summary>
        /// Builds expressions for IEnumerable fields (collections).
        /// </summary>
        private static System.Linq.Expressions.Expression BuildEnumerableExpression(Expression r, MemberExpression left, Type tProp, ILogger logger)
        {
            var ienumerable = tProp.GetInterface("IEnumerable`1");
            logger?.LogDebug("SmartPlaylist field {Field}: Type={Type}, IEnumerable={IsEnumerable}, Operator={Operator}", 
                r.MemberName, tProp.Name, ienumerable != null, r.Operator);
                
            if (ienumerable == null)
            {
                logger?.LogDebug("SmartPlaylist field {Field} is not IEnumerable", r.MemberName);
                return null;
            }
            
            if (ienumerable.GetGenericArguments()[0] == typeof(string))
            {
                return BuildStringEnumerableExpression(r, left, logger);
            }
            else
            {
                return BuildGenericEnumerableExpression(r, left, ienumerable, logger);
            }
        }

        /// <summary>
        /// Builds expressions for string IEnumerable fields.
        /// </summary>
        private static System.Linq.Expressions.Expression BuildStringEnumerableExpression(Expression r, MemberExpression left, ILogger logger)
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
            
            logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for string IEnumerable field '{Field}'", r.Operator, r.MemberName);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for string IEnumerable field '{r.MemberName}'");
        }

        /// <summary>
        /// Builds expressions for generic IEnumerable fields.
        /// </summary>
        private static System.Linq.Expressions.Expression BuildGenericEnumerableExpression(Expression r, MemberExpression left, Type ienumerable, ILogger logger)
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
            
            logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for generic IEnumerable field '{Field}'", r.Operator, r.MemberName);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for generic IEnumerable field '{r.MemberName}'");
        }

        /// <summary>
        /// Builds expressions using standard .NET operators for other field types.
        /// </summary>
        private static BinaryExpression BuildStandardOperatorExpression(Expression r, MemberExpression left, Type tProp, ILogger logger)
        {
            // Check if the operator is a known .NET operator
            logger?.LogDebug("SmartPlaylist checking if {Operator} is a built-in .NET ExpressionType", r.Operator);
            if (Enum.TryParse(r.Operator, out ExpressionType tBinary))
            {
                logger?.LogDebug("SmartPlaylist {Operator} IS a built-in ExpressionType: {ExpressionType}", r.Operator, tBinary);
                var right = System.Linq.Expressions.Expression.Constant(Convert.ChangeType(r.TargetValue, tProp));
                // use a binary operation, e.g. 'Equal' -> 'u.Age == 15'
                return System.Linq.Expressions.Expression.MakeBinary(tBinary, left, right);
            }
            
            // All supported operators have been handled explicitly above
            // If we reach here, the operator is not supported for this field type
            logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for field '{Field}' of type '{Type}'", r.Operator, r.MemberName, tProp.Name);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for field '{r.MemberName}' of type '{tProp.Name}'. Supported operators depend on the field type.");
        }

        /// <summary>
        /// Checks if a field name is a date field that needs special handling.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a date field, false otherwise</returns>
        private static bool IsDateField(string fieldName)
        {
            return FieldDefinitions.IsDateField(fieldName);
        }

        /// <summary>
        /// Converts a date string (YYYY-MM-DD) to Unix timestamp.
        /// </summary>
        /// <param name="dateString">The date string to convert</param>
        /// <returns>Unix timestamp in seconds</returns>
        /// <exception cref="ArgumentException">Thrown when the date string is invalid</exception>
        private static double ConvertDateStringToUnixTimestamp(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString))
            {
                throw new ArgumentException("Date string cannot be null or empty");
            }

            try
            {
                // Parse the date string as YYYY-MM-DD format
                if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    // Convert to Unix timestamp using UTC to ensure consistency with other date operations
                    return new DateTimeOffset(parsedDate, TimeSpan.Zero).ToUnixTimeSeconds();
                }
                else
                {
                    throw new ArgumentException($"Invalid date format: {dateString}. Expected format: YYYY-MM-DD");
                }
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"Failed to parse date string '{dateString}': {ex.Message}", ex);
            }
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