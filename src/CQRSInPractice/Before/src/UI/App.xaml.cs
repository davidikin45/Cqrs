﻿using UI.Api;

namespace UI
{
    public sealed partial class App
    {
        public App()
        {
            ApiClient.Init("http://localhost:52335/api/students");
        }
    }
}
