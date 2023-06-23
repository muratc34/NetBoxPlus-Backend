using Microsoft.EntityFrameworkCore;
using MovieAPI.Infrastructure;
using MovieAPI.Model;
using Nest;

namespace MovieAPI.Extensions
{
    public static class ElasticSearchExtensions
    {
        public static void AddElasticSearch(this IServiceCollection services, IConfiguration configuration)
        {
            var url = configuration["Elasticsearch:Url"];
            var defaultIndex = configuration["Elasticsearch:DefaultIndex"];

            var settings = new ConnectionSettings(new Uri(url!))
                .PrettyJson()
                .DefaultIndex(defaultIndex);

            AddDefaultMappings(settings);

            var client = new ElasticClient(settings);

            services.AddSingleton<IElasticClient>(client);

            CreateIndex(client, defaultIndex!);
        }

        private static void AddDefaultMappings(ConnectionSettings settings)
        {
            settings.DefaultMappingFor<Movie>(m => m);
        }

        private static void CreateIndex(IElasticClient client, string indexName)
        {
            
            if (!client.Indices.Exists(indexName).Exists)
            {
                client.Indices.Create(indexName, index => index.Map<Movie>(x => x.AutoMap()));

                using (MovieContext context = new MovieContext())
                {
                    var movies = context.Movies.ToList();

                    client.Bulk(bulk => bulk.IndexMany(movies, (bd, movie) => bd.Index(indexName)));
                }
            }
            
        }
    }
}
