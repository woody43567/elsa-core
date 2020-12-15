using Elsa.Activities.MassTransit.Consumers;
using MassTransit;
using System;
using System.Linq;

namespace Elsa.Activities.MassTransit.Extensions
{
    public static class TypeExtensions 
    {
        public static void MapEndpointConvention(this Type messageType, Uri destinationAddress)
        {
            var method = typeof(EndpointConvention).GetMethod("Map", new[] { typeof(Uri) });
            var generic = method.MakeGenericMethod(messageType);
            generic.Invoke(null, new object[] { destinationAddress });
        }

        public static Type CreateConsumerType(this Type messageType)
        {
            return typeof(WorkflowConsumer<>).MakeGenericType(messageType);
        }

        private class SimpleTypeComparer : System.Collections.Generic.IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                if (x.IsGenericParameter)
                    return true;

                return x.Assembly == y.Assembly &&
                    x.Namespace == y.Namespace &&
                    x.Name == y.Name;
            }

            public int GetHashCode(Type obj)
            {
                throw new NotImplementedException();
            }
        }

        public static System.Reflection.MethodInfo GetGenericMethod(this Type type, string name, Type[] parameterTypes)
        {
            var methods = type.GetMethods();

            
            foreach (var method in methods.Where(m => m.Name == name))
            {
                var methodParameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();

                if (methodParameterTypes.SequenceEqual(parameterTypes, new SimpleTypeComparer()))
                {
                    return method;
                }
            }

            return null;
        }
    }
}