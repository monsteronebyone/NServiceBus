﻿using System;
using FluentNHibernate.Cfg.Db;
using NHibernate;
using NHibernate.Connection;
using NHibernate.Dialect;
using NHibernate.Impl;
using NServiceBus.Config.ConfigurationSource;
using NUnit.Framework;
using NBehave.Spec.NUnit;
using Rhino.Mocks;
using NServiceBus.Saga;

namespace NServiceBus.SagaPersisters.NHibernate.Tests
{
    [TestFixture]
    public class When_configuring_the_saga_persister
    {
        private Configure config;

        [SetUp]
        public void SetUp()
        {
            var properties = SQLiteConfiguration.Standard.UsingFile(".\\NServiceBus.Sagas.sqlite").ToProperties();

            config = Configure.With(new[] { typeof(MySaga).Assembly})
                .SpringBuilder()
                .Sagas()
                .NHibernateSagaPersisterWithSQLiteAndAutomaticSchemaGeneration();
        }

        [Test]
        public void Persister_should_be_registered_as_single_call()
        {
            var persister = config.Builder.Build<SagaPersister>();

            persister.ShouldNotBeTheSameAs(config.Builder.Build<SagaPersister>());
        }

        [Test]
        public void The_sessionfactory_should_be_built_and_registered_as_singleton()
        {
            var sessionFactory = config.Builder.Build<ISessionFactory>();

            sessionFactory.ShouldNotBeNull();
            sessionFactory.ShouldBeTheSameAs(config.Builder.Build<ISessionFactory>());

        }

        [Test]
        public void Update_schema_can_be_specified_by_the_user()
        {

        }

        public class MySagaData : ISagaEntity
        {
            public System.Guid Id { get; set; }
            public string OriginalMessageId { get; set; }
            public string Originator { get; set; }
        }

        public class MySaga : Saga<MySagaData>
        {
            public override void Timeout(object state)
            {
            }
        }
    }
}
