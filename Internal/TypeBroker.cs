#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;

namespace zb.Reflect.Internal
{
    ///
    /// NOT THREAD SAFE.
    ///
    internal class TypeBroker
    {

        //TODO: generic property, field, method, event, delegate

        private Dictionary<string, CallSite<Func<CallSite, object, object?>>> CachedGetters { set; get; }

        private Dictionary<string, CallSite<Func<CallSite, object, object?, object?>>> CachedSetters { set; get; }

        private Dictionary<string, MethodInfo> CachedMethods { set; get; }

        private Dictionary<string, PropertyInfo> CachedProperties { set; get; }

        private Dictionary<string, FieldInfo> CachedFields { set; get; }

        private Dictionary<string, EventInfo> CachedEvents { set; get; }

        private Dictionary<string, Delegate> CachedDelegates { set; get; }

        private Type ClientType { get; set; }

        private bool IsNonpublicDisclosed { get; set; }

        private bool IsLazyCaching { set; get; }

        public TypeBroker(Type clientType)
        {
            if (null == clientType)
            {
                throw new ArgumentNullException();
            }

            ClientType = clientType;

            CachedMethods = new Dictionary<string, MethodInfo>();
            CachedFields = new Dictionary<string, FieldInfo>();
            CachedProperties = new Dictionary<string, PropertyInfo>();
            CachedEvents = new Dictionary<string, EventInfo>();
            CachedDelegates = new Dictionary<string, Delegate>();

            CachedSetters = new Dictionary<string, CallSite<Func<CallSite, object, object?, object?>>>();
            CachedGetters = new Dictionary<string, CallSite<Func<CallSite, object, object?>>>();
        }

        #region public members

        public object? GetField(object target, string name, bool isPrivate = false)
        {
            var fi = GetCachedField(name);
            return fi.GetValue(fi.IsStatic ? null : target);
        }

        public void SetField(object target, string name, object? value)
        {
            var fi = GetCachedField(name);
            if (!fi.IsInitOnly && fi.IsLiteral)
            {
                throw new InvalidOperationException($"Cannot set value to field {name}.");
            }
            fi.SetValue(fi.IsStatic ? null : target, value);
        }

        public object? GetProperty(object target, string name)
        {
            var pi = GetCachedProperty(name);
            if (null == pi.GetMethod)
            {
                throw new InvalidOperationException($"{name} is a write only property.");
            }
            return pi.GetMethod.Invoke(target, null);
        }

        public void SetProperty(object? target, string name, object? value)
        {
            var pi = GetCachedProperty(name);
            if (null == pi.SetMethod)
            {
                throw new InvalidOperationException($"{name} is a read only property.");
            }
            pi.SetMethod.Invoke(target, new object?[] { value });
        }

        /// <summary>
        /// This method is about 30% faster than SetProperty()
        /// </summary>
        public void SetPropertyF(object target, string name, object? value)
        {
            var setter = GetCachedSetter(name);
            setter.Target(setter, target, value);
        }

        /// <summary>
        /// This method is about 30% faster than GetProperty()
        /// </summary>
        public object? GetPropertyF(object target, string name)
        {
            var getter = GetCachedGetter(name);
            return getter.Target(getter, target);
        }

        public object? InvokeByKey(object target, string methodKey, object?[]? args = null)
        {
            var mi = CachedMethods[methodKey];
            return mi.Invoke(mi.IsStatic ? null : target, args);
        }

        public (string, MethodInfo) GetCachedMethod(string name, Type[]? types = null, ParameterModifier[]? modifiers = null)
        {
            var key = ReflectUtil.MakeMethodSignatureKey(name, types);
            if (CachedMethods.ContainsKey(key))
            {
                return (key, CachedMethods[key]);
            }

            var mi = GetMethodInfo(name, types, modifiers);
            if (null == mi)
            {
                throw new InvalidOperationException($"Method ({name}) was not found.");
            }

            CachedMethods.Add(key, mi);

            return (key, mi);
        }

        public object? InvokeMethod(
            object target,
            string name,
            object?[]? args = null,
            Type[]? types = null,
            ParameterModifier[]? modifiers = null)
        {
            if (null == types && null != args)
            {
                types = args.Select(a =>
                {
                    if (null == a) throw new InvalidOperationException($"Type of argument cannot be inferred from null.");
                    return a.GetType();
                }).ToArray();
            };

            var mi = GetMethodInfo(name, types, modifiers);
            return mi.Invoke(mi.IsStatic ? null : target, args);
        }

        public void RaiseEvent(object target, string name, object?[]? args = null)
        {
            var ei = GetCachedEvent(name);
            ei.RaiseMethod?.Invoke(ei.RaiseMethod.IsStatic ? null : target, args);
        }

        public void AddEvent(object target, string name, Delegate handler)
        {
            var ei = GetCachedEvent(name);
            if (null == ei.AddMethod)
            {
                throw new InvalidOperationException($"Cannot add event to {name}");
            }
            ei.AddEventHandler(ei.AddMethod.IsStatic ? null : target, handler);
        }

        public void RemoveEvent(object target, string name, Delegate handler)
        {
            var ei = GetCachedEvent(name);
            if (null == ei.RemoveMethod)
            {
                throw new InvalidOperationException($"Cannot remove event to {name}");
            }
            ei.RemoveEventHandler(ei.RemoveMethod.IsStatic ? null : target, handler);
        }

        // TODO: catch MethodInfo to make it a bit faster?
        public object? CallDeleget(object target, string name, object?[]? args = null)
        {
            var dt = GetCachedeDelegate(target, name);
            return dt.DynamicInvoke(args);
        }

        public void AddDelegate(object target, string name, Delegate addon)
        {
            var dt = GetCachedeDelegate(target, name);
            Delegate.Combine(dt, addon);
        }

        public void RemoveDelegate(object target, string name, Delegate del)
        {
            var dt = GetCachedeDelegate(target, name);
            Delegate.Remove(dt, del);
        }

        #endregion

        #region  private members

        private MethodInfo GetMethodInfo(string name, Type[]? types = null, ParameterModifier[]? modifiers = null)
        {
            var flags = BindingFlags.Public
                      | BindingFlags.Instance
                      | BindingFlags.NonPublic
                      | BindingFlags.Static
                      | BindingFlags.FlattenHierarchy
                      ;
            MethodInfo? mi;
            if (null == types)
            {
                mi = ClientType.GetMethod(name, flags);
            }
            else
            {
                mi = ClientType.GetMethod(
                       name,
                       flags,
                       null,
                       CallingConventions.Any,
                       types,
                       modifiers);
                if (null == mi)
                {
                    mi = GetMethodInfo(name, types, flags);
                }
            }
            if (null == mi)
            {
                throw new InvalidOperationException($"No method name {name} was found.");
            }
            return mi;
        }

        private MethodInfo? GetMethodInfo(string name, Type[] types, BindingFlags flags)
        {
            MethodInfo? rst = null;

            var methods = ClientType.GetMember(name, MemberTypes.Method, flags);
            foreach (MethodInfo mi in methods)
            {
                rst = mi;
                var ps = mi.GetParameters();
                if (ps.Length == types.Length)
                {
                    rst = mi;
                    for (var i = 0; i < ps.Length; i++)
                    {
                        var pt = ps[i].ParameterType;
                        if (pt.ContainsGenericParameters)
                        {
                            rst = MakeGenericMethod(mi, types);
                            break;
                        }
                        else if (pt.IsGenericParameter)
                        {
                            var gp = rst.GetGenericArguments();
                            if (!IsCompatibleParameters(gp, types))
                            {
                                rst = null;
                                break;
                            }
                        }
                        else if (!pt.IsAssignableFrom(types[i]))
                        {
                            rst = null;
                            break;
                        }
                    }
                    if (null != rst)
                    {
                        break;
                    }
                }
            }

            return rst;
        }

        private MethodInfo? MakeGenericMethod(MethodInfo mi, Type[] prms)
        {
            MethodInfo? rst = null;
            List<Type> ps = new List<Type>();

            var pp = mi.GetParameters();
            if (pp.Length != prms.Length)
            {
                throw new InvalidOperationException("Invalid paramter.");
            }

            for (var i = 0; i < pp.Length; i++)
            {
                if (pp[i].ParameterType.ContainsGenericParameters)
                {
                    ps.Add(prms[i]);
                }
                else if (!prms[i].IsAssignableFrom(pp[i].ParameterType))
                {
                    ps.Clear();
                    break;
                }
            }
            if (ps.Count > 0)
            {
                rst = mi.MakeGenericMethod(ps.ToArray());
            }
            return rst;
        }

        private bool IsCompatibleParameters(Type[] tp1, Type[] tp2)
        {
            var ret = true;
            if (tp1.Length == tp2.Length)
            {
                for (var i = 0; i < tp1.Length; i++)
                {
                    if (!tp1[i].IsAssignableFrom(tp2[i]))
                    {
                        ret = false;
                        break;
                    }
                }
            }
            return ret;
        }

        private PropertyInfo GetCachedProperty(string name)
        {
            PropertyInfo? pi;
            if (!CachedProperties.TryGetValue(name, out pi))
            {
                pi = ClientType.GetProperty(
                    name,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static);
                if (null == pi)
                {
                    throw new InvalidOperationException($"Cannot find property ({name})");
                }
                CachedProperties.Add(name, pi);
            }
            return pi;
        }

        private CallSite<Func<CallSite, object, object?, object?>> GetCachedSetter(string name)
        {
            CallSite<Func<CallSite, object, object?, object?>>? setter = null;
            if (!CachedSetters.TryGetValue(name, out setter))
            {
                var cs = Microsoft.CSharp.RuntimeBinder.Binder.SetMember(
                    CSharpBinderFlags.None,
                    name,
                    ClientType,
                    new CSharpArgumentInfo[]
                    {
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null),
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                    }
                );
                setter = CallSite<Func<CallSite, object, object?, object?>>.Create(cs);
                CachedSetters.Add(name, setter);
            }
            return setter;
        }

        private CallSite<Func<CallSite, object, object?>> GetCachedGetter(string name)
        {
            CallSite<Func<CallSite, object, object?>>? getter = null;
            if (!CachedGetters.TryGetValue(name, out getter))
            {
                var cs = Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
                    CSharpBinderFlags.None,
                    name,
                    ClientType,
                    new CSharpArgumentInfo[]
                    {
                        CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null)
                    }
                );
                getter = CallSite<Func<CallSite, object, object?>>.Create(cs);
                CachedGetters.Add(name, getter);
            }
            return getter;
        }

        private FieldInfo GetCachedField(string name)
        {
            FieldInfo? fi;
            if (!CachedFields.TryGetValue(name, out fi))
            {
                fi = ClientType.GetField(
                        name,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.Static);
                if (null == fi)
                {
                    throw new InvalidOperationException($"No field named {name} was found.");
                }
                CachedFields.Add(name, fi);
            }
            return fi;
        }

        /// TODO: cache "Invoke" to make it a bit faster?
        private Delegate GetCachedeDelegate(object target, string name)
        {
            Delegate? dt;
            if (CachedDelegates.TryGetValue(name, out dt))
            {
                var fi = ClientType.GetField(
                            name,
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance |
                            BindingFlags.Static);
                if (null != fi)
                {
                    if (typeof(Delegate).IsAssignableFrom(fi.FieldType))
                    {
                        throw new InvalidOperationException($"{name} is not of delegate.");
                    }
                    dt = (Delegate?)fi.GetValue(fi.IsStatic ? null : target);
                }
                if (null == dt)
                {
                    var pi = ClientType.GetProperty(
                        name,
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance |
                        BindingFlags.Static
                    );
                    if (null != pi && null != pi.GetMethod)
                    {
                        if (typeof(Delegate).IsAssignableFrom(pi.PropertyType))
                        {
                            throw new InvalidOperationException($"{name} is not of delegate.");
                        }
                        dt = (Delegate?)pi.GetValue(pi.GetMethod.IsStatic ? null : target);
                    }
                }
            }
            if (null == dt)
            {
                throw new InvalidOperationException($"No delegate named {name} was found.");
            }
            CachedDelegates.Add(name, dt);
            return dt;
        }

        private EventInfo GetCachedEvent(string name)
        {
            EventInfo? ei;
            if (!CachedEvents.TryGetValue(name, out ei))
            {
                ei = ClientType.GetEvent(
                    name,
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.Instance |
                    BindingFlags.Static
                );
            }
            if (null == ei)
            {
                throw new InvalidOperationException($"No event named {name} was found.");
            }
            CachedEvents.Add(name, ei);
            return ei;
        }

        #endregion
    }
}

#nullable disable