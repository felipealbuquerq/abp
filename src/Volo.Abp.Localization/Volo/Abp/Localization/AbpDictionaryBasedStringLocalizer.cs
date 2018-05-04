﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Localization;

namespace Volo.Abp.Localization
{
    public class AbpDictionaryBasedStringLocalizer : IStringLocalizer
    {
        public LocalizationResource Resource { get; }

        public List<IStringLocalizer> BaseLocalizers { get; }

        public virtual LocalizedString this[string name] => GetLocalizedString(name);

        public virtual LocalizedString this[string name, params object[] arguments] => GetLocalizedStringFormatted(name, arguments);

        public AbpDictionaryBasedStringLocalizer(LocalizationResource resource, List<IStringLocalizer> baseLocalizers)
        {
            Resource = resource;
            BaseLocalizers = baseLocalizers;
        }

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return GetAllStrings(CultureInfo.CurrentUICulture.Name, includeParentCultures);
        }

        public IStringLocalizer WithCulture(CultureInfo culture)
        {
            return new CultureWrapperStringLocalizer(culture.Name, this);
        }

        protected virtual LocalizedString GetLocalizedStringFormatted(string name, params object[] arguments)
        {
            return GetLocalizedStringFormatted(name, CultureInfo.CurrentUICulture.Name, arguments);
        }

        protected virtual LocalizedString GetLocalizedStringFormatted(string name, string cultureName, params object[] arguments)
        {
            var localizedString = GetLocalizedString(name, cultureName);
            return new LocalizedString(name, string.Format(localizedString.Value, arguments), localizedString.ResourceNotFound, localizedString.SearchedLocation);
        }

        protected virtual LocalizedString GetLocalizedString(string name)
        {
            return GetLocalizedString(name, CultureInfo.CurrentUICulture.Name);
        }

        protected virtual LocalizedString GetLocalizedString(string name, string cultureName)
        {
            var value = GetLocalizedStringOrNull(name, cultureName);

            if (value == null)
            {
                foreach (var baseLocalizer in BaseLocalizers)
                {
                    var baseLocalizedString = baseLocalizer.WithCulture(CultureInfo.GetCultureInfo(cultureName))[name];
                    if (baseLocalizedString != null && !baseLocalizedString.ResourceNotFound)
                    {
                        return baseLocalizedString;
                    }
                }

                return new LocalizedString(name, name, resourceNotFound: true);
            }

            return value;
        }

        protected virtual LocalizedString GetLocalizedStringOrNull(string name, string cultureName, bool tryDefaults = true)
        {
            var dictionaries = Resource.DictionaryProvider.Dictionaries;

            //Try to get from original dictionary (with country code)
            if (dictionaries.TryGetValue(cultureName, out var originalDictionary))
            {
                var strOriginal = originalDictionary.GetOrNull(name);
                if (strOriginal != null)
                {
                    return new LocalizedString(name, strOriginal.Value);
                }
            }

            if (!tryDefaults)
            {
                return null;
            }

            //Try to get from same language dictionary (without country code)
            if (cultureName.Contains("-")) //Example: "tr-TR" (length=5)
            {
                if (dictionaries.TryGetValue(GetBaseCultureName(cultureName), out var langDictionary))
                {
                    var strLang = langDictionary.GetOrNull(name);
                    if (strLang != null)
                    {
                        return new LocalizedString(name, strLang.Value);
                    }
                }
            }

            //Try to get from default language
            if (!Resource.DefaultCultureName.IsNullOrEmpty())
            {
                var defaultDictionary = dictionaries.GetOrDefault(Resource.DefaultCultureName);
                if (defaultDictionary != null)
                {
                    var strDefault = defaultDictionary.GetOrNull(name);
                    if (strDefault != null)
                    {
                        return new LocalizedString(name, strDefault.Value);
                    }
                }
            }

            //Not found
            return null;
        }

        protected virtual IReadOnlyList<LocalizedString> GetAllStrings(string cultureName, bool includeParentCultures = true)
        {
            //TODO: Can be optimized (example: if it's already default dictionary, skip overriding)

            var dictionaries = Resource.DictionaryProvider.Dictionaries;

            var allStrings = new Dictionary<string, LocalizedString>();

            foreach (var baseLocalizer in BaseLocalizers.Select(l => l.WithCulture(CultureInfo.GetCultureInfo(cultureName))))
            {
                foreach (var localizedString in baseLocalizer.GetAllStrings(includeParentCultures))
                {
                    allStrings[localizedString.Name] = localizedString;
                }
            }

            if (includeParentCultures)
            {
                //Fill all strings from default dictionary
                if (!Resource.DefaultCultureName.IsNullOrEmpty())
                {
                    var defaultDictionary = dictionaries.GetOrDefault(Resource.DefaultCultureName);
                    if (defaultDictionary != null)
                    {
                        foreach (var defaultDictString in defaultDictionary.GetAllStrings())
                        {
                            allStrings[defaultDictString.Name] = new LocalizedString(defaultDictString.Name, defaultDictString.Value);
                        }
                    }
                }

                //Overwrite all strings from the language based on country culture
                if (cultureName.Contains("-"))
                {
                    if (dictionaries.TryGetValue(GetBaseCultureName(cultureName), out var langDictionary))
                    {
                        foreach (var langString in langDictionary.GetAllStrings())
                        {
                            allStrings[langString.Name] = new LocalizedString(langString.Name, langString.Value);
                        }
                    }
                }
            }

            //Overwrite all strings from the original dictionary
            if (dictionaries.TryGetValue(cultureName, out var originalDictionary))
            {
                foreach (var originalLangString in originalDictionary.GetAllStrings())
                {
                    allStrings[originalLangString.Name] = new LocalizedString(originalLangString.Name, originalLangString.Value);
                }
            }

            return allStrings.Values.ToImmutableList();
        }

        protected virtual string GetBaseCultureName(string cultureName)
        {
            return cultureName.Contains("-")
                ? cultureName.Left(cultureName.IndexOf("-", StringComparison.Ordinal))
                : cultureName;
        }

        public class CultureWrapperStringLocalizer : IStringLocalizer
        {
            private readonly string _cultureName;
            private readonly AbpDictionaryBasedStringLocalizer _innerLocalizer;

            LocalizedString IStringLocalizer.this[string name] => _innerLocalizer.GetLocalizedString(name, _cultureName);

            LocalizedString IStringLocalizer.this[string name, params object[] arguments] => _innerLocalizer.GetLocalizedStringFormatted(name, _cultureName, arguments);

            public CultureWrapperStringLocalizer(string cultureName, AbpDictionaryBasedStringLocalizer innerLocalizer)
            {
                _cultureName = cultureName;
                _innerLocalizer = innerLocalizer;
            }

            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            {
                return _innerLocalizer.GetAllStrings(_cultureName, includeParentCultures);
            }

            public IStringLocalizer WithCulture(CultureInfo culture)
            {
                return new CultureWrapperStringLocalizer(culture.Name, _innerLocalizer);
            }
        }
    }
}