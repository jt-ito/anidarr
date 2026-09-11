using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class TableMapperFixture
    {
        public class EmbeddedType : IEmbeddedDocument
        {
        }

        public class TypeWithAllMappableProperties
        {
            public string PropString { get; set; }
            public int PropInt { get; set; }
            public bool PropBool { get; set; }
            public int? PropNullable { get; set; }
            public EmbeddedType Embedded { get; set; }
            public List<EmbeddedType> EmbeddedList { get; set; }
        }

        public class TypeWithNoMappableProperties
        {
            public Series Series { get; set; }

            public int ReadOnly { get; private set; }
            public int WriteOnly { private get; set; }
        }

        [SetUp]
        public void Setup()
        {
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<List<EmbeddedType>>());
            SqlMapper.AddTypeHandler(new EmbeddedDocumentConverter<EmbeddedType>());
        }

        [Test]
        public void test_mappable_types()
        {
            var properties = typeof(TypeWithAllMappableProperties).GetProperties();
            properties.Should().NotBeEmpty();
            properties.Should().OnlyContain(c => c.IsMappableProperty());
        }

        [Test]
        public void test_un_mappable_types()
        {
            var properties = typeof(TypeWithNoMappableProperties).GetProperties();
            properties.Should().NotBeEmpty();
            properties.Should().NotContain(c => c.IsMappableProperty());
        }

        [Test]
        public void all_basic_repositories_should_have_table_mappings()
        {
            TableMapping.Map();
            var repoTypes = typeof(TableMapping).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.BaseType != null)
                .Where(t =>
                {
                    var baseType = t.BaseType;
                    while (baseType != null)
                    {
                        if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(BasicRepository<>))
                        {
                            return true;
                        }

                        baseType = baseType.BaseType;
                    }

                    return false;
                });

            foreach (var repoType in repoTypes)
            {
                var baseType = repoType.BaseType;
                while (baseType != null && (!baseType.IsGenericType || baseType.GetGenericTypeDefinition() != typeof(BasicRepository<>)))
                {
                    baseType = baseType.BaseType;
                }

                var modelType = baseType.GetGenericArguments()[0];
                var tableName = TableMapping.Mapper.TableNameMapping(modelType);
                tableName.Should().NotBeNullOrWhiteSpace($"Model {modelType.Name} used by {repoType.Name} must be registered in TableMapping");

                Action selectTemplate = () => TableMapping.Mapper.SelectTemplate(modelType);
                selectTemplate.Should().NotThrow($"SelectTemplate for {modelType.Name} should not throw");

                Action deleteTemplate = () => TableMapping.Mapper.DeleteTemplate(modelType);
                deleteTemplate.Should().NotThrow($"DeleteTemplate for {modelType.Name} should not throw");

                Action pageCountTemplate = () => TableMapping.Mapper.PageCountTemplate(modelType);
                pageCountTemplate.Should().NotThrow($"PageCountTemplate for {modelType.Name} should not throw");
            }
        }

        [Test]
        public void unregistered_type_should_throw_informative_exception()
        {
            TableMapping.Map();
            Action selectTemplate = () => TableMapping.Mapper.SelectTemplate(typeof(TableMapperFixture));
            selectTemplate.Should().Throw<InvalidOperationException>()
                .WithMessage("*No table mapping found for type*");

            Action deleteTemplate = () => TableMapping.Mapper.DeleteTemplate(typeof(TableMapperFixture));
            deleteTemplate.Should().Throw<InvalidOperationException>()
                .WithMessage("*No table mapping found for type*");

            Action pageCountTemplate = () => TableMapping.Mapper.PageCountTemplate(typeof(TableMapperFixture));
            pageCountTemplate.Should().Throw<InvalidOperationException>()
                .WithMessage("*No table mapping found for type*");
        }
    }
}
