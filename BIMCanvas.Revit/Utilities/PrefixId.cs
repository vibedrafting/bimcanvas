using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIMCanvas.Revit.Utilities
{
    /// <summary>
    /// 数据ID生成器
    /// 每个前缀维护独立的计数器
    /// </summary>
    public static class PrefixId
    {
        private static readonly object _lockObject = new object();
        private static readonly Dictionary<string, int> _prefixCounters = new Dictionary<string, int>();
        private static string _defaultPrefix = "D";
        private static int _defaultDigits = 3;

        /// <summary>
        /// 生成新的数据ID
        /// </summary>
        /// <returns>格式为 D001, D002... 的ID</returns>
        public static string NewId()
        {
            return NewId(_defaultPrefix, _defaultDigits);
        }

        /// <summary>
        /// 生成新的数据ID，使用指定前缀
        /// </summary>
        /// <param name="prefix">ID前缀</param>
        /// <returns>格式为 [prefix]001, [prefix]002... 的ID</returns>
        public static string NewId(string prefix)
        {
            return NewId(prefix, _defaultDigits);
        }

        /// <summary>
        /// 生成新的数据ID，使用指定前缀和数字位数
        /// </summary>
        /// <param name="prefix">ID前缀</param>
        /// <param name="digits">数字部分位数</param>
        /// <returns>格式为 [prefix][数字] 的ID</returns>
        public static string NewId(string prefix, int digits)
        {
            lock (_lockObject)
            {
                if (!_prefixCounters.ContainsKey(prefix))
                {
                    _prefixCounters[prefix] = 0;
                }

                _prefixCounters[prefix]++;
                return $"{prefix}{_prefixCounters[prefix].ToString($"D{digits}")}";
            }
        }

        /// <summary>
        /// 重置计数器
        /// </summary>
        /// <param name="prefix">要重置的前缀，为空则重置所有前缀</param>
        /// <param name="startValue">起始值，默认为 0</param>
        public static void Reset(string prefix = null, int startValue = 0)
        {
            lock (_lockObject)
            {
                if (prefix == null)
                {
                    // 无参数调用时重置所有前缀
                    var keys = _prefixCounters.Keys.ToList();
                    foreach (var key in keys)
                    {
                        _prefixCounters[key] = startValue;
                    }
                }
                else
                {
                    // 指定前缀时只重置该前缀
                    _prefixCounters[prefix] = startValue;
                }
            }
        }

        /// <summary>
        /// 设置默认前缀和数字位数
        /// </summary>
        /// <param name="prefix">默认前缀</param>
        /// <param name="digits">默认数字位数</param>
        public static void SetDefaults(string prefix, int digits = 3)
        {
            lock (_lockObject)
            {
                _defaultPrefix = prefix ?? "D";
                _defaultDigits = digits > 0 ? digits : 3;
            }
        }

        /// <summary>
        /// 获取指定前缀的当前计数器值
        /// </summary>
        /// <param name="prefix">前缀，为空则获取默认前缀的计数</param>
        /// <returns>当前计数器值</returns>
        public static int GetCurrentCounter(string prefix = null)
        {
            lock (_lockObject)
            {
                string targetPrefix = prefix ?? _defaultPrefix;
                return _prefixCounters.ContainsKey(targetPrefix) ? _prefixCounters[targetPrefix] : 0;
            }
        }

        /// <summary>
        /// 获取所有前缀及其计数器值
        /// </summary>
        /// <returns>前缀和计数器的字典</returns>
        public static Dictionary<string, int> GetAllCounters()
        {
            lock (_lockObject)
            {
                return new Dictionary<string, int>(_prefixCounters);
            }
        }
    }
}
