using System.Collections.Generic;
using Jellyfin.Plugin.SmartLists.Core.QueryEngine;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    /// <summary>
    /// Represents a set of expressions that are evaluated together as a group.
    /// </summary>
    public class ExpressionSet
    {
        /// <summary>
        /// Gets the list of expressions in this set.
        /// May be null during JSON deserialization of legacy data.
        /// </summary>
        public List<Expression>? Expressions { get; init; } = [];
    }
}

