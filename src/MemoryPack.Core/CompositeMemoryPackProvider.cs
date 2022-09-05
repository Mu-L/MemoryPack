﻿using System.Collections.Concurrent;

namespace MemoryPack;

// used only default CompositeProvider
public interface IRegistableMemoryPackFormatterProvider : IMemoryPackFormatterProvider
{
    void Register(IEnumerable<IMemoryPackFormatter> formatters, IEnumerable<IMemoryPackFormatterProvider> providers);
}

public static class RegistableMemoryPackFormatterProviderExtensions
{
    public static void Register(this IRegistableMemoryPackFormatterProvider provider, params IMemoryPackFormatter[] formatters)
    {
        provider.Register(formatters, Array.Empty<IMemoryPackFormatterProvider>());
    }

    public static void Register(this IRegistableMemoryPackFormatterProvider provider, params IMemoryPackFormatterProvider[] providers)
    {
        provider.Register(Array.Empty<IMemoryPackFormatter>(), providers);
    }
}

public sealed class CompositeMemoryPackProvider : IMemoryPackFormatterProvider
{
    public static IMemoryPackFormatterProvider Create(params IMemoryPackFormatter[] formatters)
    {
        return Create(formatters, Array.Empty<IMemoryPackFormatterProvider>());
    }

    public static IMemoryPackFormatterProvider Create(params IMemoryPackFormatterProvider[] providers)
    {
        return Create(Array.Empty<IMemoryPackFormatter>(), providers);
    }

    public static IMemoryPackFormatterProvider Create(IEnumerable<IMemoryPackFormatter> formatters, IEnumerable<IMemoryPackFormatterProvider> providers)
    {
        // use ToArray, don't allow modify elements externally.
        return new CompositeMemoryPackProvider(formatters.ToArray(), providers.ToArray());
    }

    readonly (IMemoryPackFormatter[] formatters, IMemoryPackFormatterProvider[] providers) factoryArgs;
    readonly ConcurrentDictionary<Type, IMemoryPackFormatter?> cache;

    CompositeMemoryPackProvider(IMemoryPackFormatter[] formatters, IMemoryPackFormatterProvider[] providers)
    {
        this.factoryArgs = (formatters, providers);
        this.cache = new ConcurrentDictionary<Type, IMemoryPackFormatter?>();
    }

    public IMemoryPackFormatter<T>? GetFormatter<T>()
    {
        return cache.GetOrAdd(typeof(T), FormatterFactory<T>.formatterFactory, factoryArgs) as IMemoryPackFormatter<T>;
    }

    static class FormatterFactory<T>
    {
        public static readonly Func<Type, (IMemoryPackFormatter[], IMemoryPackFormatterProvider[]), IMemoryPackFormatter<T>?> formatterFactory = CreateFormatter;

        static IMemoryPackFormatter<T>? CreateFormatter(Type type, (IMemoryPackFormatter[], IMemoryPackFormatterProvider[]) args)
        {
            foreach (var item in args.Item1)
            {
                if (item is IMemoryPackFormatter<T> f)
                {
                    return f;
                }
            }

            foreach (var item in args.Item2)
            {
                var f = item.GetFormatter<T>();
                if (f != null)
                {
                    return f;
                }
            }

            return null;
        }
    }

    // static-composite(fastest, Default)

    public static readonly IRegistableMemoryPackFormatterProvider Default = new StaticCompositeMemoryPackProvider();

    sealed class StaticCompositeMemoryPackProvider : IRegistableMemoryPackFormatterProvider
    {
        static IMemoryPackFormatter[] formatters = Array.Empty<IMemoryPackFormatter>();
        static IMemoryPackFormatterProvider[] providers = Array.Empty<IMemoryPackFormatterProvider>();
        static bool freezed;

        public void Register(IEnumerable<IMemoryPackFormatter> formatters, IEnumerable<IMemoryPackFormatterProvider> providers)
        {
            if (freezed)
            {
                // To be precise, the Freeze decision is delayed until GetFormatter is called first.
                // This is to avoid exceptions in hot reload environments such as Unity, where Startup is temporarily called multiple times.
                throw new InvalidOperationException("Already registered, CompositeMemoryPackProvider.Default can only register once.");
            }

            ArgumentNullException.ThrowIfNull(formatters);
            ArgumentNullException.ThrowIfNull(providers);

            StaticCompositeMemoryPackProvider.formatters = formatters.ToArray();
            StaticCompositeMemoryPackProvider.providers = providers.ToArray();
        }

        public IMemoryPackFormatter<T>? GetFormatter<T>()
        {
            return Cache<T>.formatter;
        }

        static class Cache<T>
        {
            public static readonly IMemoryPackFormatter<T>? formatter;

            static Cache()
            {
                freezed = true;
                foreach (var item in formatters)
                {
                    if (item is IMemoryPackFormatter<T> f)
                    {
                        formatter = f;
                        return;
                    }
                }

                foreach (var item in providers)
                {
                    var f = item.GetFormatter<T>();
                    if (f != null)
                    {
                        formatter = f;
                        return;
                    }
                }
            }
        }
    }
}