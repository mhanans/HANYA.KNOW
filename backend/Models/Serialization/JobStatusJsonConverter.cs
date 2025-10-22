using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace backend.Models.Serialization;

public sealed class JobStatusJsonConverter : JsonConverter<JobStatus>
{
    public override JobStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var stringValue = reader.GetString();
                if (!string.IsNullOrWhiteSpace(stringValue) && Enum.TryParse<JobStatus>(stringValue, true, out var statusFromString))
                {
                    return statusFromString;
                }
                break;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var numericValue) && Enum.IsDefined(typeof(JobStatus), numericValue))
                {
                    return (JobStatus)numericValue;
                }
                break;
        }

        throw new JsonException($"Unable to convert value to {nameof(JobStatus)}.");
    }

    public override void Write(Utf8JsonWriter writer, JobStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
