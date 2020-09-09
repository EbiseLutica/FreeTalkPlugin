using System.Collections.Generic;
using System.Linq;

namespace FreeTalkPlugin
{
    public static class Helper
    {
        public static IEnumerable<KeyValuePair<string, string>> BuildKeyValues(params (string, string)[] kvs) => kvs.Select(kvs => KeyValuePair.Create(kvs.Item1, kvs.Item2));
    }
}
