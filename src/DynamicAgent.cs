using System;
using System.Dynamic;
using zb.Logger;

namespace zb.Reflect
{
    public class DynamicAgent : DynamicObject
    {
        private ReflectProxy Proxy { set; get; }

        private object Client { set; get; }

        public DynamicAgent(object client)
        {
            Proxy = new ReflectProxy(client);
            Client = client;
        }

        public new Type GetType()
        {
            return Client.GetType();
        }

        public new string ToString()
        {
            return Client.ToString();
        }

        #region Dynamic members

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = null;
            bool ret = true;
            try
            {
                result = Proxy.GetValue(binder.Name);
            }
            catch (Exception)
            {
                ret = false;
            }

            return ret;
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            bool ret = true;
            try
            {
                Proxy.SetValue(binder.Name, value);
            }
            catch (Exception)
            {
                ret = false;
            }
            return ret;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            bool ret = true;
            result = null;

            try
            {
                result = Proxy.InvokeMethod(binder.Name, args);
            }
            catch (Exception e)
            {
                ret = false;
                Log.E(e.ToString());
            }

            return ret;
        }

        #endregion
    }
}
