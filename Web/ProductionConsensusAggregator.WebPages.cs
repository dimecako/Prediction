using System.Net;

public partial class Program
{
    private static string GetHomePage()
    {
        string today =
            DateTime.Today.ToString("yyyy-MM-dd");

        return $@"
            <!DOCTYPE html>
            <html>
            <head>

            <meta charset='utf-8'>
            <meta name='viewport'
                content='width=device-width, initial-scale=1'>

            <title>Football AI</title>

            <style>

            body {{
                margin: 0;
                font-family: Arial, sans-serif;
                background: #0f172a;
                color: white;
            }}

            .container {{
                max-width: 700px;
                margin: 70px auto;
                padding: 20px;
            }}

            .card {{
                background: #1e293b;
                padding: 35px;
                border-radius: 16px;
            }}

            h1 {{
                margin-top: 0;
                color: #38bdf8;
            }}

            input {{
                width: 100%;
                box-sizing: border-box;
                padding: 14px;
                margin-top: 10px;
                margin-bottom: 20px;
                border-radius: 8px;
                border: 1px solid #475569;
                background: #0f172a;
                color: white;
                font-size: 18px;
            }}

            button {{
                width: 100%;
                padding: 15px;
                border: 0;
                border-radius: 8px;
                background: #0284c7;
                color: white;
                font-size: 18px;
                font-weight: bold;
                cursor: pointer;
            }}

            button:hover {{
                background: #0369a1;
            }}

            .small {{
                color: #94a3b8;
                margin-top: 20px;
            }}

            </style>

            </head>

            <body>

            <div class='container'>

            <div class='card'>

            <h1>⚽ Football AI</h1>

            <p>
            Select prediction date:
            </p>

            <input
                id='analysisDate'
                type='date'
                value='{today}'
                required>

            <button
                type='button'
                onclick=""window.location.href='/analyse?date=' + document.getElementById('analysisDate').value"">
                ANALYSE
            </button>

            <div class='small'>
            Forebet · Statarea · PredictZ · WinDrawWin
            </div>

            </div>

            </div>

            </body>
            </html>";
    }


    private static string GetResultPage(
        string date,
        string result)
    {
        string encoded =
            WebUtility.HtmlEncode(result);

        return $@"
            <!DOCTYPE html>

            <html>

            <head>

            <meta charset='utf-8'>

            <meta name='viewport'
                content='width=device-width, initial-scale=1'>

            <title>Football AI - {date}</title>

            <style>

            body {{
                margin: 0;
                background: #0f172a;
                color: #e2e8f0;
                font-family: Arial, sans-serif;
            }}

            .container {{
                max-width: 1400px;
                margin: auto;
                padding: 25px;
            }}

            h1 {{
                color: #38bdf8;
            }}

            a {{
                color: #38bdf8;
                text-decoration: none;
            }}

            pre {{
                background: #020617;
                padding: 20px;
                border-radius: 12px;
                overflow-x: auto;
                font-size: 14px;
                line-height: 1.5;
            }}

            </style>

            </head>

            <body>

            <div class='container'>

            <h1>⚽ Football AI</h1>

            <h2>
            Prediction date: {date}
            </h2>

            <p>
            /
            ← New analysis
            </a>
            </p>

            <pre>{encoded}</pre>

            </div>

            </body>

            </html>";
    }


    private static string GetErrorPage(string error)
    {
        return $@"
            <!DOCTYPE html>

            <html>

            <head>

            <meta charset='utf-8'>

            <title>Football AI Error</title>

            </head>

            <body>

            <h1>Football AI</h1>

            <h2>Analysis failed</h2>

            <pre>
            {WebUtility.HtmlEncode(error)}
            </pre>

            /
            Back
            </a>

            </body>

            </html>";
    }
}