using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.SmartPlaylist.Constants;

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
        
        private static System.Linq.Expressions.Expression BuildExpr<T>(Expression r, ParameterExpression param, string defaultUserId, ILogger logger = null)
        {
            // Check if this is a user-specific field that should always use method calls
            if (Expression.IsUserSpecificField(r.MemberName))
            {
                // Use the specified user ID or default to playlist owner
                var effectiveUserId = r.UserId ?? defaultUserId;
                if (string.IsNullOrEmpty(effectiveUserId))
                {
                    logger?.LogError("SmartPlaylist user-specific field '{Field}' requires a valid user ID", r.MemberName);
                    throw new ArgumentException($"User-specific field '{r.MemberName}' requires a valid user ID, but no user ID was provided and no default user ID is available.");
                }
                
                // Create a new expression with all properties copied and effective user ID set
                var userSpecificExpression = new Expression(r.MemberName, r.Operator, r.TargetValue)
                {
                    UserId = effectiveUserId,
                    IncludeUnwatchedSeries = r.IncludeUnwatchedSeries
                };
                
                return BuildUserSpecificExpression<T>(userSpecificExpression, param, logger);
            }

            // Get the property/field expression for non-user-specific fields
            var left = System.Linq.Expressions.Expression.PropertyOrField(param, r.MemberName);
            var tProp = left.Type;
            
            logger?.LogDebug("SmartPlaylist BuildExpr: Field={Field}, Type={Type}, Operator={Operator}", r.MemberName, tProp.Name, r.Operator);

            // Handle different field types with specialized handlers
            // Check resolution fields first (before generic string check)
            if (tProp == typeof(string) && IsResolutionField(r.MemberName))
            {
                return BuildResolutionExpression(r, left, logger);
            }
            
            // Check framerate fields (nullable float type)
            if (tProp == typeof(float?) && IsFramerateField(r.MemberName))
            {
                return BuildFramerateExpression(r, left, logger);
            }
            
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
            else if (returnType == typeof(double) && r.MemberName == "LastPlayedDate")
            {
                return BuildUserSpecificLastPlayedDateExpression(r, methodCall, logger);
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
            if (r.Operator != "Equal" && r.Operator != "NotEqual")
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for boolean user-specific field '{Field}'", r.Operator, r.MemberName);
                var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for boolean user-specific field '{r.MemberName}'. Supported operators: {supportedOperators}");
            }
            
            var boolValue = ValidateAndParseBooleanValue(r.TargetValue, r.MemberName, logger);
            var right = System.Linq.Expressions.Expression.Constant(boolValue);
            return r.Operator == "Equal"
                ? System.Linq.Expressions.Expression.MakeBinary(ExpressionType.Equal, methodCall, right)
                : System.Linq.Expressions.Expression.MakeBinary(ExpressionType.NotEqual, methodCall, right);
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
                var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for integer user-specific field '{r.MemberName}'. Supported operators: {supportedOperators}");
            }
        }

        /// <summary>
        /// Builds expressions for user-specific LastPlayedDate fields with special "never played" handling.
        /// </summary>
        private static BinaryExpression BuildUserSpecificLastPlayedDateExpression(Expression r, System.Linq.Expressions.Expression methodCall, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling user-specific LastPlayedDate field {Field} with value {Value}", r.MemberName, r.TargetValue);
            
            // Create the "never played" check: methodCall != -1
            var neverPlayedCheck = System.Linq.Expressions.Expression.NotEqual(
                methodCall, 
                System.Linq.Expressions.Expression.Constant(-1.0)
            );
            
            // Build the main date expression using a simplified version for method calls
            var mainExpression = BuildDateExpressionForMethodCall(r, methodCall, logger);
            
            // Combine: (methodCall != -1) AND (main date condition)
            return System.Linq.Expressions.Expression.AndAlso(neverPlayedCheck, mainExpression);
        }

        /// <summary>
        /// Builds date expressions for method calls (user-specific LastPlayedDate).
        /// </summary>
        private static BinaryExpression BuildDateExpressionForMethodCall(Expression r, System.Linq.Expressions.Expression methodCall, ILogger logger)
        {
            // Handle relative date operators
            if (r.Operator == "NewerThan" || r.Operator == "OlderThan")
            {
                return BuildRelativeDateExpressionForMethodCall(r, methodCall, logger);
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
            
            // Handle basic date operators
            var right = System.Linq.Expressions.Expression.Constant(targetTimestamp);
            
            return r.Operator switch
            {
                "After" => System.Linq.Expressions.Expression.GreaterThan(methodCall, right),
                "Before" => System.Linq.Expressions.Expression.LessThan(methodCall, right),
                _ when Enum.TryParse(r.Operator, out ExpressionType dateBinary) => 
                    System.Linq.Expressions.Expression.MakeBinary(dateBinary, methodCall, right),
                _ => throw new ArgumentException($"Operator '{r.Operator}' is not currently supported for user-specific LastPlayedDate field. Supported operators: {Operators.GetSupportedOperatorsString(r.MemberName)}")
            };
        }

        /// <summary>
        /// Builds relative date expressions for method calls (NewerThan, OlderThan).
        /// </summary>
        private static BinaryExpression BuildRelativeDateExpressionForMethodCall(Expression r, System.Linq.Expressions.Expression methodCall, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling '{Operator}' for user-specific field {Field} with value {Value}", r.Operator, r.MemberName, r.TargetValue);
            
            // Use shared helper to parse relative date and get cutoff timestamp
            var cutoffTimestamp = ParseRelativeDateAndGetCutoffTimestamp(r, logger);
            var cutoffConstant = System.Linq.Expressions.Expression.Constant(cutoffTimestamp);
            
            if (r.Operator == "NewerThan")
            {
                // methodCall >= cutoffTimestamp (more recent than cutoff)
                return System.Linq.Expressions.Expression.GreaterThanOrEqual(methodCall, cutoffConstant);
            }
            else
            {
                // methodCall < cutoffTimestamp (older than cutoff)
                return System.Linq.Expressions.Expression.LessThan(methodCall, cutoffConstant);
            }
        }

        /// <summary>
        /// Parses relative date string (e.g., "3:days", "1:month") and returns cutoff timestamp.
        /// </summary>
        private static double ParseRelativeDateAndGetCutoffTimestamp(Expression r, ILogger logger)
        {
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
            
            return cutoffTimestamp;
        }

        /// <summary>
        /// Builds expressions for string fields.
        /// </summary>
        private static System.Linq.Expressions.Expression BuildStringExpression(Expression r, MemberExpression left, ILogger logger)
        {
            // Enforce per-field operator whitelist for string fields
            var allowedOps = Operators.GetOperatorsForField(r.MemberName);
            if (!allowedOps.Contains(r.Operator))
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for string field '{Field}'. Allowed: {Allowed}",
                    r.Operator, r.MemberName, string.Join(", ", allowedOps));
                var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for string field '{r.MemberName}'. Supported operators: {supportedOperators}");
            }

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
                case "IsIn":
                    logger?.LogDebug("SmartPlaylist applying string IsIn to {Field} with value '{Value}'", r.MemberName, r.TargetValue);
                    var isInMethod = typeof(Engine).GetMethod("StringIsInList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var targetValueConstant = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                    return System.Linq.Expressions.Expression.Call(isInMethod, left, targetValueConstant);
                case "IsNotIn":
                    logger?.LogDebug("SmartPlaylist applying string IsNotIn to {Field} with value '{Value}'", r.MemberName, r.TargetValue);
                    var isNotInMethod = typeof(Engine).GetMethod("StringIsInList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var targetValueConstant2 = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                    var isNotInCall = System.Linq.Expressions.Expression.Call(isNotInMethod, left, targetValueConstant2);
                    return System.Linq.Expressions.Expression.Not(isNotInCall);
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
            if (r.Operator != "Equal" && r.Operator != "NotEqual")
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for boolean field '{Field}'", r.Operator, r.MemberName);
                var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for boolean field '{r.MemberName}'. Supported operators: {supportedOperators}");
            }
            
            var boolValue = ValidateAndParseBooleanValue(r.TargetValue, r.MemberName, logger);
            var right = System.Linq.Expressions.Expression.Constant(boolValue);
            return r.Operator == "Equal"
                ? System.Linq.Expressions.Expression.MakeBinary(ExpressionType.Equal, left, right)
                : System.Linq.Expressions.Expression.MakeBinary(ExpressionType.NotEqual, left, right);
        }

        /// <summary>
        /// Builds expressions for date fields (stored as Unix timestamps).
        /// </summary>
        private static BinaryExpression BuildDateExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling date field {Field} with value {Value}", r.MemberName, r.TargetValue);
            
            // Special handling for LastPlayedDate: exclude items that have never been played (value = -1)
            if (r.MemberName == "LastPlayedDate")
            {
                var neverPlayedCheck = System.Linq.Expressions.Expression.NotEqual(
                    left, 
                    System.Linq.Expressions.Expression.Constant(-1.0)
                );
                
                // Build the main date expression using the standard logic below
                var mainExpression = BuildStandardDateExpression(r, left, logger);
                
                // Combine: (LastPlayedDate != -1) AND (main date condition)
                return System.Linq.Expressions.Expression.AndAlso(neverPlayedCheck, mainExpression);
            }
            
            return BuildStandardDateExpression(r, left, logger);
        }

        /// <summary>
        /// Builds expressions for resolution fields that support both equality and numeric comparisons.
        /// </summary>
        private static BinaryExpression BuildResolutionExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling resolution field {Field} with value {Value}", r.MemberName, r.TargetValue);
            
            // Enforce per-field operator whitelist for resolution fields
            var allowedOps = Operators.GetOperatorsForField(r.MemberName);
            if (!allowedOps.Contains(r.Operator))
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for resolution field '{Field}'. Allowed: {Allowed}",
                    r.Operator, r.MemberName, string.Join(", ", allowedOps));
                var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for resolution field '{r.MemberName}'. Supported operators: {supportedOperators}");
            }

            // Get the numeric height value for the target resolution
            var targetHeight = ResolutionTypes.GetHeightForResolution(r.TargetValue);
            if (targetHeight == -1)
            {
                logger?.LogError("SmartPlaylist resolution comparison failed: Invalid resolution value '{Value}' for field '{Field}'", r.TargetValue, r.MemberName);
                throw new ArgumentException($"Invalid resolution value '{r.TargetValue}' for field '{r.MemberName}'. Expected one of: {string.Join(", ", ResolutionTypes.GetAllValues())}");
            }

            // For all resolution comparisons, we need to ensure the resolution field is not null/empty
            // and that it's a valid resolution (height > 0)
            var resolutionHeightMethod = typeof(ResolutionTypes).GetMethod("GetHeightForResolution", [typeof(string)]);
            var resolutionHeightCall = System.Linq.Expressions.Expression.Call(
                resolutionHeightMethod, 
                left
            );

            var targetHeightConstant = System.Linq.Expressions.Expression.Constant(targetHeight);
            var zeroConstant = System.Linq.Expressions.Expression.Constant(0);

            // First, ensure the resolution is valid (not null/empty and height > 0)
            var isValidResolution = System.Linq.Expressions.Expression.GreaterThan(resolutionHeightCall, zeroConstant);

            // Handle different operators with validity check
            BinaryExpression comparisonExpression = r.Operator switch
            {
                "Equal" => System.Linq.Expressions.Expression.Equal(resolutionHeightCall, targetHeightConstant),
                "NotEqual" => System.Linq.Expressions.Expression.NotEqual(resolutionHeightCall, targetHeightConstant),
                "GreaterThan" => System.Linq.Expressions.Expression.GreaterThan(resolutionHeightCall, targetHeightConstant),
                "LessThan" => System.Linq.Expressions.Expression.LessThan(resolutionHeightCall, targetHeightConstant),
                "GreaterThanOrEqual" => System.Linq.Expressions.Expression.GreaterThanOrEqual(resolutionHeightCall, targetHeightConstant),
                "LessThanOrEqual" => System.Linq.Expressions.Expression.LessThanOrEqual(resolutionHeightCall, targetHeightConstant),
                _ => throw new ArgumentException($"Operator '{r.Operator}' is not supported for resolution field '{r.MemberName}'. Supported operators: {string.Join(", ", allowedOps)}")
            };

            // Combine: resolution must be valid AND meet the comparison criteria
            return System.Linq.Expressions.Expression.AndAlso(isValidResolution, comparisonExpression);
        }

        /// <summary>
        /// Builds expressions for framerate fields that support numeric comparisons with null handling.
        /// Items with null framerate are ignored (filtered out).
        /// </summary>
        private static BinaryExpression BuildFramerateExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling framerate field {Field} with value {Value}", r.MemberName, r.TargetValue);
            
            // Enforce per-field operator whitelist for framerate fields
            var allowedOps = Operators.GetOperatorsForField(r.MemberName);
            if (!allowedOps.Contains(r.Operator))
            {
                logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for framerate field '{Field}'. Allowed: {Allowed}",
                    r.Operator, r.MemberName, string.Join(", ", allowedOps));
                var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                throw new ArgumentException($"Operator '{r.Operator}' is not supported for framerate field '{r.MemberName}'. Supported operators: {supportedOperators}");
            }

            // Parse target value as float using culture-invariant parsing
            if (!float.TryParse(r.TargetValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var targetValue))
            {
                logger?.LogError("SmartPlaylist framerate comparison failed: Invalid numeric value '{Value}' for field '{Field}'", r.TargetValue, r.MemberName);
                throw new ArgumentException($"Invalid numeric value '{r.TargetValue}' for field '{r.MemberName}'. Expected a decimal number.");
            }

            // For all framerate comparisons, we need to ensure the framerate field is not null
            // This means items with null framerate will be ignored
            var hasValueCheck = System.Linq.Expressions.Expression.Property(left, "HasValue");
            var valueProperty = System.Linq.Expressions.Expression.Property(left, "Value");
            var targetConstant = System.Linq.Expressions.Expression.Constant(targetValue);

            // Handle different operators with null check
            BinaryExpression comparisonExpression = r.Operator switch
            {
                "Equal" => System.Linq.Expressions.Expression.Equal(valueProperty, targetConstant),
                "NotEqual" => System.Linq.Expressions.Expression.NotEqual(valueProperty, targetConstant),
                "GreaterThan" => System.Linq.Expressions.Expression.GreaterThan(valueProperty, targetConstant),
                "LessThan" => System.Linq.Expressions.Expression.LessThan(valueProperty, targetConstant),
                "GreaterThanOrEqual" => System.Linq.Expressions.Expression.GreaterThanOrEqual(valueProperty, targetConstant),
                "LessThanOrEqual" => System.Linq.Expressions.Expression.LessThanOrEqual(valueProperty, targetConstant),
                _ => throw new ArgumentException($"Operator '{r.Operator}' is not supported for framerate field '{r.MemberName}'. Supported operators: {string.Join(", ", allowedOps)}")
            };

            // Combine: framerate must have a value (not null) AND meet the comparison criteria
            return System.Linq.Expressions.Expression.AndAlso(hasValueCheck, comparisonExpression);
        }
        
        /// <summary>
        /// Builds standard date expressions without special handling for never-played items.
        /// </summary>
        private static BinaryExpression BuildStandardDateExpression(Expression r, MemberExpression left, ILogger logger)
        {
            
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

            else if (r.Operator == "After")
            {
                logger?.LogDebug("SmartPlaylist 'After' operator for date field {Field} with timestamp {Timestamp}", r.MemberName, targetTimestamp);
                var right = System.Linq.Expressions.Expression.Constant(targetTimestamp);
                return System.Linq.Expressions.Expression.GreaterThan(left, right);
            }
            else if (r.Operator == "Before")
            {
                logger?.LogDebug("SmartPlaylist 'Before' operator for date field {Field} with timestamp {Timestamp}", r.MemberName, targetTimestamp);
                var right = System.Linq.Expressions.Expression.Constant(targetTimestamp);
                return System.Linq.Expressions.Expression.LessThan(left, right);
            }
            else
            {
                // For other operators (legacy .NET ExpressionType), use the exact timestamp comparison
                if (Enum.TryParse(r.Operator, out ExpressionType dateBinary))
                {
                    logger?.LogDebug("SmartPlaylist {Operator} IS a built-in ExpressionType for date field: {ExpressionType}", r.Operator, dateBinary);
                    var right = System.Linq.Expressions.Expression.Constant(targetTimestamp);
                    return System.Linq.Expressions.Expression.MakeBinary(dateBinary, left, right);
                }
                else
                {
                    logger?.LogError("SmartPlaylist unsupported date operator '{Operator}' for field '{Field}'", r.Operator, r.MemberName);
                    var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
                    throw new ArgumentException($"Operator '{r.Operator}' is not supported for date field '{r.MemberName}'. Supported operators: {supportedOperators}");
                }
            }
        }

        /// <summary>
        /// Builds expressions for relative date operators (NewerThan, OlderThan).
        /// </summary>
        private static BinaryExpression BuildRelativeDateExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling '{Operator}' for field {Field} with value {Value}", r.Operator, r.MemberName, r.TargetValue);
            
            // Use shared helper to parse relative date and get cutoff timestamp
            var cutoffTimestamp = ParseRelativeDateAndGetCutoffTimestamp(r, logger);
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
            if (!DateTime.TryParseExact(r.TargetValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, 
                DateTimeStyles.None, out DateTime targetDate))
            {
                logger?.LogError("SmartPlaylist date equality failed: Invalid date format '{Value}' for field '{Field}'", r.TargetValue, r.MemberName);
                throw new ArgumentException($"Invalid date format '{r.TargetValue}' for field '{r.MemberName}'. Expected format: YYYY-MM-DD");
            }
            
            var startOfDay = (double)new DateTimeOffset(targetDate, TimeSpan.Zero).ToUnixTimeSeconds();
            var endOfDay = (double)new DateTimeOffset(targetDate.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
            
            logger?.LogDebug("SmartPlaylist date equality range: {StartOfDay} to {EndOfDay} (exclusive)", startOfDay, endOfDay);
            
            // Create expression: operand.DateCreated >= startOfDay && operand.DateCreated < endOfDay
            var startConstant = System.Linq.Expressions.Expression.Constant(startOfDay);
            var endConstant = System.Linq.Expressions.Expression.Constant(endOfDay);
            
            var greaterThanOrEqual = System.Linq.Expressions.Expression.GreaterThanOrEqual(left, startConstant);
            var lessThan = System.Linq.Expressions.Expression.LessThan(left, endConstant);
            
            return System.Linq.Expressions.Expression.AndAlso(greaterThanOrEqual, lessThan);
        }

        /// <summary>
        /// Builds expressions for date inequality (outside date ranges).
        /// </summary>
        private static BinaryExpression BuildDateInequalityExpression(Expression r, MemberExpression left, ILogger logger)
        {
            logger?.LogDebug("SmartPlaylist handling date inequality for field {Field} with date {Date}", r.MemberName, r.TargetValue);
            
            // For inequality, we need to check if the date is outside the target day
            if (!DateTime.TryParseExact(r.TargetValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, 
                DateTimeStyles.None, out DateTime targetDate))
            {
                logger?.LogError("SmartPlaylist date inequality failed: Invalid date format '{Value}' for field '{Field}'", r.TargetValue, r.MemberName);
                throw new ArgumentException($"Invalid date format '{r.TargetValue}' for field '{r.MemberName}'. Expected format: YYYY-MM-DD");
            }
            
            var startOfDay = (double)new DateTimeOffset(targetDate, TimeSpan.Zero).ToUnixTimeSeconds();
            var endOfDay = (double)new DateTimeOffset(targetDate.AddDays(1), TimeSpan.Zero).ToUnixTimeSeconds();
            
            logger?.LogDebug("SmartPlaylist date inequality range: < {StartOfDay} or >= {EndOfDay}", startOfDay, endOfDay);
            
            // Create expression: operand.DateCreated < startOfDay || operand.DateCreated >= endOfDay
            var startConstant = System.Linq.Expressions.Expression.Constant(startOfDay);
            var endConstant = System.Linq.Expressions.Expression.Constant(endOfDay);
            
            var lessThan = System.Linq.Expressions.Expression.LessThan(left, startConstant);
            var greaterThanOrEqual = System.Linq.Expressions.Expression.GreaterThanOrEqual(left, endConstant);
            
            return System.Linq.Expressions.Expression.OrElse(lessThan, greaterThanOrEqual);
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

            if (r.Operator == "IsIn")
            {
                logger?.LogDebug("SmartPlaylist applying collection IsIn to {Field} with value '{Value}'", r.MemberName, r.TargetValue);
                var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                var method = typeof(Engine).GetMethod("AnyItemIsInList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    logger?.LogError("SmartPlaylist AnyItemIsInList method not found!");
                    throw new InvalidOperationException("AnyItemIsInList method not found");
                }
                return System.Linq.Expressions.Expression.Call(method, left, right);
            }
            
            if (r.Operator == "IsNotIn")
            {
                logger?.LogDebug("SmartPlaylist applying collection IsNotIn to {Field} with value '{Value}'", r.MemberName, r.TargetValue);
                var right = System.Linq.Expressions.Expression.Constant(r.TargetValue, typeof(string));
                var method = typeof(Engine).GetMethod("AnyItemIsInList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (method == null)
                {
                    logger?.LogError("SmartPlaylist AnyItemIsInList method not found!");
                    throw new InvalidOperationException("AnyItemIsInList method not found");
                }
                var isNotInCall = System.Linq.Expressions.Expression.Call(method, left, right);
                return System.Linq.Expressions.Expression.Not(isNotInCall);
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
            
            var supportedOperators = Operators.GetOperatorsForField(r.MemberName);
            var supportedOperatorsString = string.Join(", ", supportedOperators);
            logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for string IEnumerable field '{Field}'", r.Operator, r.MemberName);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for string IEnumerable field '{r.MemberName}'. Supported operators: {supportedOperatorsString}");
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
            
            var supportedOperators = Operators.GetOperatorsForField(r.MemberName);
            var supportedOperatorsString = string.Join(", ", supportedOperators);
            logger?.LogError("SmartPlaylist unsupported operator '{Operator}' for generic IEnumerable field '{Field}'", r.Operator, r.MemberName);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for generic IEnumerable field '{r.MemberName}'. Supported operators: {supportedOperatorsString}");
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
            var supportedOperators = Operators.GetSupportedOperatorsString(r.MemberName);
            throw new ArgumentException($"Operator '{r.Operator}' is not supported for field '{r.MemberName}' of type '{tProp.Name}'. Supported operators: {supportedOperators}");
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
        /// Checks if a field name is a resolution field that needs special handling.
        /// </summary>
        /// <param name="fieldName">The field name to check</param>
        /// <returns>True if it's a resolution field, false otherwise</returns>
        private static bool IsResolutionField(string fieldName)
        {
            return FieldDefinitions.IsResolutionField(fieldName);
        }

        private static bool IsFramerateField(string fieldName)
        {
            return FieldDefinitions.IsFramerateField(fieldName);
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
                if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, out DateTime parsedDate))
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

        public static Func<T, bool> CompileRule<T>(Expression r, string defaultUserId, ILogger logger = null)
        {
            var paramUser = System.Linq.Expressions.Expression.Parameter(typeof(T));
            var expr = BuildExpr<T>(r, paramUser, defaultUserId, logger);
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

        /// <summary>
        /// Helper method for string IsIn operator - checks if a single string contains any item from a semicolon-separated list.
        /// </summary>
        /// <param name="fieldValue">The field value to check</param>
        /// <param name="targetList">Semicolon-separated list of values to check against</param>
        /// <returns>True if the field value contains any item in the target list</returns>
        internal static bool StringIsInList(string fieldValue, string targetList)
        {
            if (string.IsNullOrEmpty(fieldValue) || string.IsNullOrEmpty(targetList)) 
                return false;
            
            // Split by semicolon, trim whitespace, and filter out empty strings
            var listItems = targetList.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(item => item.Trim())
                                     .Where(item => !string.IsNullOrEmpty(item))
                                     .ToList();
            
            if (listItems.Count == 0) 
                return false;
            
            // Check if fieldValue contains any item in the list (case insensitive, partial matching)
            return listItems.Any(item => fieldValue.Contains(item, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Helper method for collection IsIn operator - checks if any item in a collection contains any item from a semicolon-separated list.
        /// </summary>
        /// <param name="collection">The collection of strings to check</param>
        /// <param name="targetList">Semicolon-separated list of values to check against</param>
        /// <returns>True if any item in the collection contains any item in the target list</returns>
        internal static bool AnyItemIsInList(IEnumerable<string> collection, string targetList)
        {
            if (collection == null || string.IsNullOrEmpty(targetList)) 
                return false;
            
            // Split by semicolon, trim whitespace, and filter out empty strings
            var listItems = targetList.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(item => item.Trim())
                                     .Where(item => !string.IsNullOrEmpty(item))
                                     .ToList();
            
            if (listItems.Count == 0) 
                return false;
            
            // Check if any item in the collection contains any item in the target list (case insensitive, partial matching)
            return collection.Any(collectionItem => 
                collectionItem != null && 
                listItems.Any(targetItem => 
                    collectionItem.Contains(targetItem, StringComparison.OrdinalIgnoreCase)));
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