using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

using Microsoft.Extensions.Configuration;

using Serilog.Configuration;

namespace Serilog.Settings.Configuration;

class ObjectArgumentValue : IConfigurationArgumentValue
{
    readonly IConfigurationSection _section;
    readonly IReadOnlyCollection<Assembly> _configurationAssemblies;

    public ObjectArgumentValue(IConfigurationSection section, IReadOnlyCollection<Assembly> configurationAssemblies)
    {
        _section = section ?? throw new ArgumentNullException(nameof(section));

        // used by nested logger configurations to feed a new pass by ConfigurationReader
        _configurationAssemblies = configurationAssemblies ?? throw new ArgumentNullException(nameof(configurationAssemblies));
    }

    public object? ConvertTo(Type toType, ResolutionContext resolutionContext)
    {
        // return the entire section for internal processing
        if (toType == typeof(IConfigurationSection)) return _section;

        // process a nested configuration to populate an Action<> logger/sink config parameter?
        var typeInfo = toType.GetTypeInfo();
        if (typeInfo.IsGenericType &&
            typeInfo.GetGenericTypeDefinition() is Type genericType && genericType == typeof(Action<>))
        {
            var configType = typeInfo.GenericTypeArguments[0];
            IConfigurationReader configReader = new ConfigurationReader(_section, _configurationAssemblies, resolutionContext);

            return configType switch
            {
                _ when configType == typeof(LoggerConfiguration) => new Action<LoggerConfiguration>(configReader.Configure),
                _ when configType == typeof(LoggerSinkConfiguration) => new Action<LoggerSinkConfiguration>(configReader.ApplySinks),
                _ when configType == typeof(LoggerEnrichmentConfiguration) => new Action<LoggerEnrichmentConfiguration>(configReader.ApplyEnrichment),
                _ => throw new ArgumentException($"Configuration resolution for Action<{configType.Name}> parameter type at the path {_section.Path} is not implemented.")
            };
        }

        if (toType.IsArray)
            return CreateArray();

        // Only build ctor expression when type is explicitly specified in _section
        if (TryBuildCtorExpression(_section, resolutionContext, out var ctorExpression))
            return RunCtorExpression(ctorExpression);

        if (IsContainer(toType, out var elementType) && TryCreateContainer(out var container))
            return container;

        // Without a type explicitly specified, attempt to create ctor expression of toType
        if (TryBuildCtorExpression(_section, toType, resolutionContext, out ctorExpression))
            return RunCtorExpression(ctorExpression);

        // MS Config binding can work with a limited set of primitive types and collections
        return _section.Get(toType);

        object CreateArray()
        {
            var arrayElementType = toType.GetElementType()!;
            var configurationElements = _section.GetChildren().ToArray();
            var array = Array.CreateInstance(arrayElementType, configurationElements.Length);
            for (int i = 0; i < configurationElements.Length; ++i)
            {
                var argumentValue = ConfigurationReader.GetArgumentValue(configurationElements[i], _configurationAssemblies);
                var value = argumentValue.ConvertTo(arrayElementType, resolutionContext);
                array.SetValue(value, i);
            }

            return array;
        }

        bool TryCreateContainer([NotNullWhen(true)] out object? result)
        {
            result = null;

            if (IsConstructableDictionary(toType, elementType, out var concreteType, out var keyType, out var valueType, out var addMethod))
            {
                result = Activator.CreateInstance(concreteType) ?? throw new InvalidOperationException($"Activator.CreateInstance returned null for {concreteType}");

                foreach (var section in _section.GetChildren())
                {
                    var argumentValue = ConfigurationReader.GetArgumentValue(section, _configurationAssemblies);
                    var key = new StringArgumentValue(section.Key).ConvertTo(keyType, resolutionContext);
                    var value = argumentValue.ConvertTo(valueType, resolutionContext);
                    addMethod.Invoke(result, new[] { key, value });
                }
                return true;
            }
            else if (IsConstructableContainer(toType, elementType, out concreteType, out addMethod))
            {
                result = Activator.CreateInstance(concreteType) ?? throw new InvalidOperationException($"Activator.CreateInstance returned null for {concreteType}");

                foreach (var section in _section.GetChildren())
                {
                    var argumentValue = ConfigurationReader.GetArgumentValue(section, _configurationAssemblies);
                    var value = argumentValue.ConvertTo(elementType, resolutionContext);
                    addMethod.Invoke(result, new[] { value });
                }
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    static object RunCtorExpression(NewExpression ctorExpression)
    {
        Expression body = ctorExpression.Type.IsValueType ? Expression.Convert(ctorExpression, typeof(object)) : ctorExpression;
        return Expression.Lambda<Func<object>>(body).Compile().Invoke();
    }

    internal static bool TryBuildCtorExpression(
        IConfigurationSection section, ResolutionContext resolutionContext, [NotNullWhen(true)] out NewExpression? ctorExpression)
    {
        var typeDirective = section.GetValue<string>("$type") switch
        {
            not null => "$type",
            null => section.GetValue<string>("type") switch
            {
                not null => "type",
                null => null,
            },
        };

        var type = typeDirective switch
        {
            not null => Type.GetType(section.GetValue<string>(typeDirective)!, throwOnError: false),
            null => null,
        };

        if (type is null or { IsAbstract: true })
        {
            ctorExpression = null;
            return false;
        }
        else
        {
            var suppliedArguments = section.GetChildren().Where(s => s.Key != typeDirective)
                .ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
            return TryBuildCtorExpression(type, suppliedArguments, resolutionContext, out ctorExpression);
        }

    }

    internal static bool TryBuildCtorExpression(
        IConfigurationSection section, Type parameterType, ResolutionContext resolutionContext, [NotNullWhen(true)] out NewExpression? ctorExpression)
    {
        var suppliedArguments = section.GetChildren()
            .ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
        return TryBuildCtorExpression(parameterType, suppliedArguments, resolutionContext, out ctorExpression);
    }

    static bool TryBuildCtorExpression(
        Type type, Dictionary<string, IConfigurationSection> suppliedArguments, ResolutionContext resolutionContext, [NotNullWhen(true)] out NewExpression? ctorExpression)
    {
        ctorExpression = null;

        if (suppliedArguments.Count == 0 &&
            type.GetConstructor(Type.EmptyTypes) is ConstructorInfo parameterlessCtor)
        {
            ctorExpression = Expression.New(parameterlessCtor);
            return true;
        }

        var ctor =
            (from c in type.GetConstructors()
             from p in c.GetParameters()
             let argumentBindResult = suppliedArguments.TryGetValue(p.Name ?? "", out var argValue) switch
             {
                 true => new { success = true, hasMatch = true, value = (object?)argValue },
                 false => p.HasDefaultValue switch
                 {
                     true  => new { success = true,  hasMatch = false, value = (object?)p.DefaultValue },
                     false => new { success = false, hasMatch = false, value = (object?)null },
                 },
             }
             group new { argumentBindResult, p.ParameterType } by c into gr
             where gr.All(z => z.argumentBindResult.success)
             let matchedArgs = gr.Where(z => z.argumentBindResult.hasMatch).ToList()
             orderby matchedArgs.Count descending,
                     matchedArgs.Count(p => p.ParameterType == typeof(string)) descending
             select new
             {
                 ConstructorInfo = gr.Key,
                 ArgumentValues = gr.Select(z => new { Value = z.argumentBindResult.value, Type = z.ParameterType })
                                    .ToList()
             }).FirstOrDefault();

        if (ctor is null)
        {
            return false;
        }

        var ctorArguments = new List<Expression>();
        foreach (var argumentValue in ctor.ArgumentValues)
        {
            if (TryBindToCtorArgument(argumentValue.Value, argumentValue.Type, resolutionContext, out var argumentExpression))
            {
                ctorArguments.Add(argumentExpression);
            }
            else
            {
                return false;
            }
        }

        ctorExpression = Expression.New(ctor.ConstructorInfo, ctorArguments);
        return true;

        static bool TryBindToCtorArgument(object value, Type type, ResolutionContext resolutionContext, [NotNullWhen(true)] out Expression? argumentExpression)
        {
            argumentExpression = null;

            if (value is IConfigurationSection s)
            {
                if (s.Value is string argValue)
                {
                    var stringArgumentValue = new StringArgumentValue(argValue);
                    try
                    {
                        argumentExpression = Expression.Constant(
                            stringArgumentValue.ConvertTo(type, resolutionContext),
                            type);

                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
                else if (s.GetChildren().Any())
                {
                    var elementType = type.GetElementType();
                    if (elementType is not null)
                    {
                        var elements = new List<Expression>();
                        foreach (var element in s.GetChildren())
                        {
                            if (TryBindToCtorArgument(element, elementType, resolutionContext, out var elementExpression))
                            {
                                elements.Add(elementExpression);
                            }
                            else
                            {
                                return false;
                            }
                        }
                        argumentExpression = Expression.NewArrayInit(elementType, elements);
                        return true;
                    }
                    if (TryBuildCtorExpression(s, resolutionContext, out var ctorExpression) || TryBuildCtorExpression(s, type, resolutionContext, out ctorExpression))
                    {
                        if (ctorExpression.Type.IsValueType && !type.IsValueType)
                        {
                            argumentExpression = Expression.Convert(ctorExpression, type);
                        }
                        else
                        {
                            argumentExpression = ctorExpression;
                        }
                        return true;
                    }
                    if (IsContainer(type, out elementType))
                    {
                        if (IsConstructableDictionary(type, elementType, out var concreteType, out var keyType, out var valueType, out var addMethod))
                        {
                            var elements = new List<ElementInit>();
                            foreach (var element in s.GetChildren())
                            {
                                if (TryBindToCtorArgument(element, valueType, resolutionContext, out var elementExpression))
                                {
                                    var key = new StringArgumentValue(element.Key).ConvertTo(keyType, resolutionContext);
                                    elements.Add(Expression.ElementInit(addMethod, Expression.Constant(key, keyType), elementExpression));
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            argumentExpression = Expression.ListInit(Expression.New(concreteType), elements);
                            return true;
                        }
                        if (IsConstructableContainer(type, elementType, out concreteType, out addMethod))
                        {
                            var elements = new List<Expression>();
                            foreach (var element in s.GetChildren())
                            {
                                if (TryBindToCtorArgument(element, elementType, resolutionContext, out var elementExpression))
                                {
                                    elements.Add(elementExpression);
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            argumentExpression = Expression.ListInit(Expression.New(concreteType), addMethod, elements);
                            return true;
                        }
                    }

                    return false;
                }
            }

            argumentExpression = Expression.Constant(value, type);
            return true;
        }
    }

    static bool IsContainer(Type type, [NotNullWhen(true)] out Type? elementType)
    {
        elementType = null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType)
            {
                if (iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    elementType = iface.GetGenericArguments()[0];
                    return true;
                }
            }
        }

        return false;
    }

    static bool IsConstructableDictionary(Type type, Type elementType, [NotNullWhen(true)] out Type? concreteType, [NotNullWhen(true)] out Type? keyType, [NotNullWhen(true)] out Type? valueType, [NotNullWhen(true)] out MethodInfo? addMethod)
    {
        concreteType = null;
        keyType = null;
        valueType = null;
        addMethod = null;
        if (!elementType.IsGenericType || elementType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
        {
            return false;
        }
        var argumentTypes = elementType.GetGenericArguments();
        keyType = argumentTypes[0];
        valueType = argumentTypes[1];
        if (type.IsAbstract)
        {
            concreteType = typeof(Dictionary<,>).MakeGenericType(argumentTypes);
            if (!type.IsAssignableFrom(concreteType))
            {
                return false;
            }
        }
        else
        {
            concreteType = type;
        }
        if (concreteType.GetConstructor(Type.EmptyTypes) == null)
        {
            return false;
        }
        foreach (var method in concreteType.GetMethods())
        {
            if (!method.IsStatic && method.Name == "Add")
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == keyType && parameters[1].ParameterType == valueType)
                {
                    addMethod = method;
                    return true;
                }
            }
        }
        return false;
    }

    static bool IsConstructableContainer(Type type, Type elementType, [NotNullWhen(true)] out Type? concreteType, [NotNullWhen(true)] out MethodInfo? addMethod)
    {
        addMethod = null;
        if (type.IsAbstract)
        {
            concreteType = typeof(List<>).MakeGenericType(elementType);
            if (!type.IsAssignableFrom(concreteType))
            {
                concreteType = typeof(HashSet<>).MakeGenericType(elementType);
                if (!type.IsAssignableFrom(concreteType))
                {
                    concreteType = null;
                    return false;
                }
            }
        }
        else
        {
            concreteType = type;
        }
        if (concreteType.GetConstructor(Type.EmptyTypes) == null)
        {
            return false;
        }
        foreach (var method in concreteType.GetMethods())
        {
            if (!method.IsStatic && method.Name == "Add")
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == elementType)
                {
                    addMethod = method;
                    return true;
                }
            }
        }
        return false;
    }
}
