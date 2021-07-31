using System;
using System.Linq;
using System.Reflection;

namespace zb.Reflect.Internal
{
    internal class ReflectUtil
    {
        public static string MakeMethodSignatureKey(string methodName, Type[] types)
        {
            string key = methodName;
            if (null != types)
                foreach (var type in types)
                {
                    key = string.Format("{0}_{1}", key, type.FullName);
                }
            return key;
        }

        public static string MakeMethodSignatureKey(string methodName, object[] args)
        {
            Type[] types = null;
            if (null != args)
            {
                types = args.Select(a => a.GetType()).ToArray();
            }
            return MakeMethodSignatureKey(methodName, types);
        }

        public static string MakeMethodSignatureKey(MethodInfo mi)
        {
            return MakeMethodSignatureKey(mi.Name, mi.GetParameters().Select(e => e.ParameterType).ToArray());
        }
    }
}
