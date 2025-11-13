using System.Collections.Generic;
using Jellyfin.Plugin.SmartLists.Core.QueryEngine;

namespace Jellyfin.Plugin.SmartLists.Core.Models
{
    public class ExpressionSet
    {
        public List<Expression> Expressions { get; set; } = [];
    }
}

