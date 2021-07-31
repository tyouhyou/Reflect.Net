#nullable enable

using System;
using System.Collections.Generic;
using zb.Reflect.Internal;

namespace zb.Reflect
{
    public class ReflectProxy
    {
        private static Dictionary<Type, TypeBroker> CachedBrokers { set; get; }
            = new Dictionary<Type, TypeBroker>();

        private object Client { set; get; }

        private TypeBroker Broker { set; get; }

        public ReflectProxy(object client, bool cacheType = false)
        {
            var type = client.GetType();
            TypeBroker? broker;
            if (!CachedBrokers.TryGetValue(type, out broker))
            {
                broker = new TypeBroker(type);
                if (cacheType)
                {
                    CachedBrokers.Add(type, broker);
                }
            }
            Client = client;
            Broker = broker;
        }

        public void SetValue(string name, object? value)
        {
            bool set = true;
            try { Broker.SetPropertyF(Client, name, value); }
            catch (InvalidOperationException) { set = false; }
            if (!set) Broker.SetField(Client, name, value);
        }

        public object? GetValue(string name)
        {
            object? rst = null;
            bool get = true;
            try { rst = Broker.GetProperty(Client, name); }
            catch (InvalidOperationException) { get = false; }
            if (!get) rst = Broker.GetField(Client, name);
            return rst;
        }

        public object? GetField(string name)
            => Broker.GetField(Client, name);

        public void SetField(string name, object? value)
            => Broker.SetField(Client, name, value);

        public object? GetProperty(string name)
            => Broker.GetProperty(Client, name);

        public void SetProperty(string name, object? value)
            => Broker.SetProperty(Client, name, value);

        public object? GetPropertyF(string name)
            => Broker.GetPropertyF(Client, name);

        public void SetPropertyF(string name, object? value)
            => Broker.SetPropertyF(Client, name, value);

        public string GetInvokeKey(string name, Type[]? types = null, System.Reflection.ParameterModifier[]? modifiers = null)
        {
            return Broker.GetCachedMethod(name, types, modifiers).Item1;
        }

        public object? InvokeByKey(string methodKey, object?[]? args)
            => Broker.InvokeByKey(Client, methodKey, args);

        public object? InvokeMethod(string name, object?[]? args = null, Type[]? types = null)
            => Broker.InvokeMethod(Client, name, args, types);

        public object? InvokeMethod(string name, Type[] types, object?[] args)
            => Client.GetType().GetMethod(name, types)?.Invoke(Client, args);

        public void RaiseEvent(string name, object?[]? args = null)
            => Broker.RaiseEvent(Client, name, args);

        public void AddEvent(string name, Delegate handler)
            => Broker.AddEvent(Client, name, handler);

        public void RemoveEvent(string name, Delegate handler)
            => Broker.RemoveEvent(Client, name, handler);

        public object? CallDeleget(string name, object?[]? args = null)
            => Broker.CallDeleget(Client, name, args);

        public void AddDelegate(string name, Delegate handler)
            => Broker.AddDelegate(Client, name, handler);

        public void RemoveDelegate(string name, Delegate handler)
            => Broker.RemoveDelegate(Client, name, handler);
    }
}

#nullable disable