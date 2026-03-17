using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GestureSign.Tests
{
    [TestClass]
    public class AppConfigTests
    {
        [TestMethod]
        public void NormalizeDefaultActivationMethod_InvalidValue_FallsBackToSafeMode()
        {
            var result = AppConfig.NormalizeDefaultActivationMethod(99);

            Assert.AreEqual((int)ActivationMethod.SafeMode, result);
        }

        [TestMethod]
        public void NormalizeDefaultActivationMethod_AttachThreadInputValue_IsPreserved()
        {
            var result = AppConfig.NormalizeDefaultActivationMethod((int)ActivationMethod.AttachThreadInput);

            Assert.AreEqual((int)ActivationMethod.AttachThreadInput, result);
        }
    }
}