using Amazon;
using Amazon.S3;
using Onlyspans.Worker.Api.Configuration;
using Onlyspans.Worker.Api.Services;

namespace Onlyspans.Worker.Api;

public static partial class Startup
{
    public static IServiceCollection AddS3Services(this IServiceCollection services, IConfiguration configuration)
    {
        var s3Options = configuration.GetSection(S3Options.SectionName).Get<S3Options>()
            ?? throw new InvalidOperationException("S3 configuration is missing");

        services.AddSingleton(s3Options);

        services.AddSingleton<IAmazonS3>(_ =>
        {
            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(s3Options.Region)
            };

            // Support local development with custom endpoint (e.g., LocalStack)
            if (!string.IsNullOrEmpty(s3Options.Endpoint))
            {
                config.ServiceURL = s3Options.Endpoint;
                config.ForcePathStyle = true;
            }

            return new AmazonS3Client(config);
        });

        services.AddScoped<ISnapshotDownloader, S3SnapshotDownloader>();

        return services;
    }
}
