using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.ExpressionTranslators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Expressions.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.ExpressionTranslators.Internal;

namespace NpgsqlSample
{
    public class NpgsqlCompositeMethodCallTranslatorEx : NpgsqlCompositeMethodCallTranslator
    {
        static readonly IMethodCallTranslator[] MethodCallTranslators =
        {
            new NpgsqlTrigramSearchMethodTranslator()
        };

        public NpgsqlCompositeMethodCallTranslatorEx(
            RelationalCompositeMethodCallTranslatorDependencies dependencies,
            INpgsqlOptions npgsqlOptions)
            : base(dependencies, npgsqlOptions)
        {
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            AddTranslators(MethodCallTranslators);
        }
    }

    public static class NpgsqlTrigramSearchLinqExtensions
    {
        public static bool FuzzyMatches(this string value, string search) => throw new NotSupportedException();
        public static double WordSimilarity(this string value, string search) => throw new NotSupportedException();
    }

    public class NpgsqlTrigramSearchMethodTranslator : IMethodCallTranslator
    {
        public Expression Translate(MethodCallExpression methodCallExpression)
        {
            if (methodCallExpression.Method.DeclaringType == typeof(NpgsqlTrigramSearchLinqExtensions)) {
                return TryTranslateOperator(methodCallExpression);
            }

            return null;
        }

        private static Expression TryTranslateOperator(MethodCallExpression methodCallExpression)
        {
            switch (methodCallExpression.Method.Name)
            {
                case nameof(NpgsqlTrigramSearchLinqExtensions.FuzzyMatches):
                    return new CustomBinaryExpression(methodCallExpression.Arguments[0], methodCallExpression.Arguments[1], "%>", typeof(bool));
                case nameof(NpgsqlTrigramSearchLinqExtensions.WordSimilarity):
                    return new CustomBinaryExpression(methodCallExpression.Arguments[0], methodCallExpression.Arguments[1], "<->>", typeof(double));
                default:
                    return null;
            }
        }
    }

    public class Entity
    {
        public int Id { get; set; }
        public string Name { get; set; }
        
    }

    public class TestContext : DbContext
    {
        public TestContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Entity> Entities { get; set; }
    }
    
    class Program
    {
        static void Main(string[] args)
        {
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddConsole());
            
            services
                .AddEntityFrameworkNpgsql()
                .AddDbContext<TestContext>(optionsBuilder =>
                {
                    optionsBuilder.UseNpgsql("Host=localhost;Database=postgres;Username=postgres;Password=postgres;");
                    optionsBuilder.ReplaceService<ICompositeMethodCallTranslator, NpgsqlCompositeMethodCallTranslatorEx>();
                });

            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var search = "foobar";
                var skip = 10;
                var take = 10;

                var context = scope.ServiceProvider.GetRequiredService<TestContext>();
                var entities = context.Entities.Where(x => context.Entities
                        .Where(y => y.Name.FuzzyMatches(search))
                        .OrderBy(y => y.Name.WordSimilarity(search))
                        .Skip(skip)
                        .Take(take)
                        .Select(y => y.Id).Contains(x.Id))
                    .OrderBy(x => x.Name.WordSimilarity(search))
                    .ToList();
            }
        }
    }
}