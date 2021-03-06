﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace OMnG
{
    public abstract class ObjectExtensionsConfiguration
    {
        #region nested types

        public class DefaultConfiguration : DelegateILCachingConfiguration
        {

        }

        public class DelegateILCachingConfiguration : ObjectExtensionsConfiguration
        {
            private static Dictionary<PropertyInfo, Func<object, object>> Getters = new Dictionary<PropertyInfo, Func<object, object>>();
            private static Dictionary<PropertyInfo, Action<object, object>> Setters = new Dictionary<PropertyInfo, Action<object, object>>();

            protected override object GetValue(PropertyInfo property, object target)
            {
                property = property ?? throw new ArgumentNullException(nameof(property));
                target = target ?? throw new ArgumentNullException(nameof(target));

                if (!property.CanRead)
                    throw new ArgumentException("The property cannot be read.", nameof(property));

                if (property.DeclaringType.IsValueType)
                    return property.GetValue(target);

                if (!Getters.ContainsKey(property))
                {
                    MethodInfo targetGetMethod = property.GetGetMethod(true);
                    DynamicMethod d = new DynamicMethod("", typeof(object), new[] { typeof(object) }, true);

                    ILGenerator ilGenerator = d.GetILGenerator();

                    ilGenerator.Emit(OpCodes.Ldarg_0);
                    ilGenerator.Emit(OpCodes.Call, targetGetMethod);

                    Type returnType = targetGetMethod.ReturnType;

                    if (returnType.IsValueType)
                    {
                        ilGenerator.Emit(OpCodes.Box, returnType);
                    }

                    ilGenerator.Emit(OpCodes.Ret);

                    Getters.Add(property, (Func<object, object>)d.CreateDelegate(typeof(Func<object, object>)));

                    return property.GetValue(target);
                }
                return Getters[property](target);
            }
            protected override void SetValue(PropertyInfo property, object target, object value)
            {
                property = property ?? throw new ArgumentNullException(nameof(property));
                target = target ?? throw new ArgumentNullException(nameof(target));

                if (!property.CanWrite)
                    throw new ArgumentException("The property cannot be set.", nameof(property));

                if (property.DeclaringType.IsValueType)
                    property.SetValue(target, value);

                if (!Setters.ContainsKey(property))
                {
                    MethodInfo targetSetMethod = property.GetSetMethod(true);
                    DynamicMethod d = new DynamicMethod("", typeof(void), new[] { typeof(object), typeof(object) }, true);
                    ILGenerator ilGenerator = d.GetILGenerator();

                    ilGenerator.Emit(OpCodes.Ldarg_0);
                    ilGenerator.Emit(OpCodes.Ldarg_1);

                    Type parameterType = property.PropertyType;

                    if (parameterType.IsValueType)
                    {
                        ilGenerator.Emit(OpCodes.Unbox_Any, parameterType);
                    }

                    ilGenerator.Emit(OpCodes.Call, targetSetMethod);
                    ilGenerator.Emit(OpCodes.Ret);
                    
                    Setters.Add(property, (Action<object, object>)d.CreateDelegate(typeof(Action<object, object>)));
                }
                Setters[property](target, value);
            }
        }
        public class DelegateCachingConfiguration : ObjectExtensionsConfiguration
        {
            private static Dictionary<PropertyInfo, Func<object, object>> Getters = new Dictionary<PropertyInfo, Func<object, object>>();
            private static Dictionary<PropertyInfo, Action<object, object>> Setters = new Dictionary<PropertyInfo, Action<object, object>>();

            protected override object GetValue(PropertyInfo property, object target)
            {
                property = property ?? throw new ArgumentNullException(nameof(property));
                target = target ?? throw new ArgumentNullException(nameof(target));

                if (!property.CanRead)
                    throw new ArgumentException("The property cannot be read.", nameof(property));

                if (property.DeclaringType.IsValueType)
                    return property.GetValue(target);

                if (!Getters.ContainsKey(property))
                {
                    Delegate d = Delegate.CreateDelegate(
                        typeof(Func<,>).MakeGenericType(property.ReflectedType, property.PropertyType),
                        null,
                        property.GetGetMethod(true));

                    ParameterExpression targetParam = Expression.Parameter(typeof(object));
                    Func<object, object> fo =
                        Expression.Lambda<Func<object, object>>(
                            Expression.Convert(
                                Expression.Invoke(
                                    Expression.Convert(
                                        Expression.Constant(d),
                                        typeof(Func<,>).MakeGenericType(property.ReflectedType, property.PropertyType)),
                                    Expression.Convert(targetParam, property.ReflectedType)),
                            typeof(object)),
                        targetParam)
                        .Compile();

                    Getters.Add(property, fo);

                    return property.GetValue(target);
                }
                return Getters[property](target);
            }
            protected override void SetValue(PropertyInfo property, object target, object value)
            {
                property = property ?? throw new ArgumentNullException(nameof(property));
                target = target ?? throw new ArgumentNullException(nameof(target));

                if (!property.CanWrite)
                    throw new ArgumentException("The property cannot be set.", nameof(property));

                if (property.DeclaringType.IsValueType)
                    property.SetValue(target, value);

                if (!Setters.ContainsKey(property))
                {
                    Delegate d = Delegate.CreateDelegate(
                        typeof(Action<,>).MakeGenericType(property.ReflectedType, property.PropertyType),
                        null,
                        property.GetSetMethod(true));

                    ParameterExpression targetParam = Expression.Parameter(typeof(object));
                    ParameterExpression valueParam = Expression.Parameter(typeof(object));
                    Action<object, object> fo =
                        Expression.Lambda<Action<object, object>>(
                                Expression.Invoke(
                                    Expression.Convert(
                                        Expression.Constant(d),
                                        typeof(Action<,>).MakeGenericType(property.ReflectedType, property.PropertyType)
                                        ),
                                    Expression.Convert(targetParam, property.ReflectedType),
                                    Expression.Convert(valueParam, property.PropertyType)
                                ),
                        targetParam, valueParam)
                        .Compile();

                    Setters.Add(property, fo);
                }
                Setters[property](target, value);
            }
        }
        public class PureReflectionConfiguration : ObjectExtensionsConfiguration
        {
            protected override object GetValue(PropertyInfo property, object target)
            {
                return property.GetValue(target);
            }
            protected override void SetValue(PropertyInfo property, object target, object value)
            {
                property.SetValue(target, value);
            }
        }

        #endregion
        
        public object Get(PropertyInfo property, object target)
        {
            return ParseValue(property, target, GetValue(property, target));
        }
        protected abstract object GetValue(PropertyInfo property, object target);
        public void Set(PropertyInfo property, object target, object value)
        {
            SetValue(property, target, ParseValue(property, target, value));
        }
        protected abstract void SetValue(PropertyInfo property, object target, object value);
        protected virtual object ParseValue(PropertyInfo property, object target, object value)
        {
            if (value == null)
                return property.PropertyType.GetDefault();
            else
            {
                if (!property.PropertyType.IsPrimitive)
                    return value;
                else
                    return Convert.ChangeType(value, property.PropertyType);
            }
        }
    }
}
