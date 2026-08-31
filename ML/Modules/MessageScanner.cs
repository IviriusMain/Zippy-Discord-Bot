using Microsoft.ML;
using Microsoft.ML.Data;
using System.IO;

public class MessageData
{
    [LoadColumn(0)]
    public bool Label { get; set; }

    [LoadColumn(1)]
    public string? MessageText { get; set; }
}

public class MessagePrediction
{
    [ColumnName("PredictedLabel")]
    public bool IsSpam { get; set; }

    public float Score { get; set; }
    public float Probability { get; set; }
}

public class MessageScanner
{
    private readonly MLContext _mlContext = new MLContext();
    private PredictionEngine<MessageData, MessagePrediction>? _predictionEngine;

    public void Initialize(string tsvPath)
    {
        if (!File.Exists(tsvPath))
        {
            throw new FileNotFoundException($"Training file not found at {tsvPath}");
        }

        IDataView dataView = _mlContext.Data.LoadFromTextFile<MessageData>(
            tsvPath,
            hasHeader: true,
            separatorChar: '\t');

        var pipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(MessageData.MessageText))
        .Append(_mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName: nameof(MessageData.Label)));

        var model = pipeline.Fit(dataView);
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<MessageData, MessagePrediction>(model);
    }

    public MessagePrediction Predict(string text)
    {
        lock (_predictionEngine!)
        {
            return _predictionEngine.Predict(new MessageData { MessageText = text });
        }
    }
}
