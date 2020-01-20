using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Storage;
using Windows.AI.MachineLearning;
namespace TheSeeingPi
{
    public sealed class MyCustomVisionModelInput
    {
        public VideoFrame Data;
    }
    public sealed class MyCustomVisionModelOutput
    {
        public TensorString ClassLabel = TensorString.Create(new long[] { 1, 1 });
        public IList<IDictionary<string, float>> Loss = new List<IDictionary<string, float>>();
    }
    public sealed class MyCustomVisionModel
    {
        private LearningModel model;
        private LearningModelSession session;
        private LearningModelBinding binding;
        public static async Task<MyCustomVisionModel> CreateFromStreamAsync(StorageFile stream)
        {
            MyCustomVisionModel learningModel = new MyCustomVisionModel();
            learningModel.model = await LearningModel.LoadFromStorageFileAsync(stream);
            learningModel.session = new LearningModelSession(learningModel.model);
            learningModel.binding = new LearningModelBinding(learningModel.session);
            return learningModel;
        }
        public async Task<MyCustomVisionModelOutput> EvaluateAsync(MyCustomVisionModelInput input)
        {
            binding.Bind("data", input.Data);
            var result = await session.EvaluateAsync(binding, "0");
            var output = new MyCustomVisionModelOutput();
            output.ClassLabel = result.Outputs["classLabel"] as TensorString;
            output.Loss = result.Outputs["loss"] as IList<IDictionary<string, float>>;
            return output;
        }
    }
}