using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;

namespace RaspberryModules.App.Modules
{
    public class SPIDisplay
    {
        /* Uncomment for Raspberry Pi 2 */
        private const string SPI_CONTROLLER_NAME = "SPI0";  /* For Raspberry Pi 2, use SPI0                             */
        private const Int32 SPI_CHIP_SELECT_LINE = 0;       /* Line 0 maps to physical pin number 24 on the Rpi2        */
        private const Int32 DATA_COMMAND_PIN = 22;          /* We use GPIO 22 since it's conveniently near the SPI pins */
        private const Int32 RESET_PIN = 23;                 /* We use GPIO 23 since it's conveniently near the SPI pins */


        /* This sample is intended to be used with the following OLED display: http://www.adafruit.com/product/938 */
        private const UInt32 SCREEN_WIDTH_PX = 128;                         /* Number of horizontal pixels on the display */
        private const UInt32 SCREEN_HEIGHT_PX = 64;                         /* Number of vertical pixels on the display   */
        private const UInt32 SCREEN_HEIGHT_PAGES = SCREEN_HEIGHT_PX / 8;    /* The vertical pixels on this display are arranged into 'pages' of 8 pixels each */
        private byte[,] DisplayBuffer =
            new byte[SCREEN_WIDTH_PX, SCREEN_HEIGHT_PAGES];                 /* A local buffer we use to store graphics data for the screen                    */
        private byte[] SerializedDisplayBuffer =
            new byte[SCREEN_WIDTH_PX * SCREEN_HEIGHT_PAGES];                /* A temporary buffer used to prepare graphics data for sending over SPI          */

        /* Definitions for SPI and GPIO */
        private SpiDevice SpiDisplay;
        private GpioController IoController;
        private GpioPin DataCommandPin;
        private GpioPin ResetPin;

        /* Display commands. See the datasheet for details on commands: http://www.adafruit.com/datasheets/SSD1306.pdf                      */
        private static readonly byte[] CMD_DISPLAY_OFF = { 0xAE };              /* Turns the display off                                    */
        private static readonly byte[] CMD_DISPLAY_ON = { 0xAF };               /* Turns the display on                                     */
        private static readonly byte[] CMD_CHARGEPUMP_ON = { 0x8D, 0x14 };      /* Turn on internal charge pump to supply power to display  */
        private static readonly byte[] CMD_MEMADDRMODE = { 0x20, 0x00 };        /* Horizontal memory mode                                   */
        private static readonly byte[] CMD_SEGREMAP = { 0xA1 };                 /* Remaps the segments, which has the effect of mirroring the display horizontally */
        private static readonly byte[] CMD_COMSCANDIR = { 0xC8 };               /* Set the COM scan direction to inverse, which flips the screen vertically        */
        private static readonly byte[] CMD_RESETCOLADDR = { 0x21, 0x00, 0x7F }; /* Reset the column address pointer                         */
        private static readonly byte[] CMD_RESETPAGEADDR = { 0x22, 0x00, 0x07 };/* Reset the page address pointer                           */

        private bool DisplayUnavailable = true;

        public void WriteLinesToScreen(List<string> lines)
        {
            if (!DisplayUnavailable)
            {
                ClearDisplayBuf();

                uint counter = 0;
                foreach (string line in lines)
                {
                    WriteLineDisplayBuf(line, 0, counter);
                    counter++;
                }
                DisplayUpdate();
            }
        }

        public async void InitAll()
        {
            try
            {
                InitGpio();             /* Initialize the GPIO controller and GPIO pins */
                await InitSpi();        /* Initialize the SPI controller                */
                await InitDisplay();    /* Initialize the display                       */
                DisplayUnavailable = false;

                WriteLineDisplayBuf("Ready...", 0, 0);
                DisplayUpdate();

            }
            /* If initialization fails, display the exception and stop running */
            catch (Exception ex)
            {

                Debug.WriteLine("Exception: " + ex.Message);

                if (ex.InnerException != null)
                {
                    Debug.WriteLine("\nInner Exception: " + ex.InnerException.Message);
                }
                return;

            }
        }

        /* Initialize the SPI bus */
        private async Task InitSpi()
        {
            try
            {
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE); /* Create SPI initialization settings                               */
                settings.ClockFrequency = 10000000;                             /* Datasheet specifies maximum SPI clock frequency of 10MHz         */
                settings.Mode = SpiMode.Mode3;                                  /* The display expects an idle-high clock polarity, we use Mode3    
                                                                                 * to set the clock polarity and phase to: CPOL = 1, CPHA = 1         
                                                                                 */
                var controller = await SpiController.GetDefaultAsync();
                SpiDisplay = controller.GetDevice(settings);  /* Create an SpiDevice with our bus controller and SPI settings */

            }
            /* If initialization fails, display the exception and stop running */
            catch (Exception ex)
            {
                throw new Exception("SPI Initialization Failed", ex);
            }
        }

        /* Send SPI commands to power up and initialize the display */
        private async Task InitDisplay()
        {
            /* Initialize the display */
            try
            {
                /* See the datasheet for more details on these commands: http://www.adafruit.com/datasheets/SSD1306.pdf             */
                await ResetDisplay();                   /* Perform a hardware reset on the display                                  */
                DisplaySendCommand(CMD_CHARGEPUMP_ON);  /* Turn on the internal charge pump to provide power to the screen          */
                DisplaySendCommand(CMD_MEMADDRMODE);    /* Set the addressing mode to "horizontal"                                  */
                DisplaySendCommand(CMD_SEGREMAP);       /* Flip the display horizontally, so it's easier to read on the breadboard  */
                DisplaySendCommand(CMD_COMSCANDIR);     /* Flip the display vertically, so it's easier to read on the breadboard    */
                DisplaySendCommand(CMD_DISPLAY_ON);     /* Turn the display on                                                      */
            }
            catch (Exception ex)
            {
                throw new Exception("Display Initialization Failed", ex);
            }
        }

        /* Perform a hardware reset of the display */
        private async Task ResetDisplay()
        {
            ResetPin.Write(GpioPinValue.Low);   /* Put display into reset                       */
            await Task.Delay(1);                /* Wait at least 3uS (We wait 1mS since that is the minimum delay we can specify for Task.Delay() */
            ResetPin.Write(GpioPinValue.High);  /* Bring display out of reset                   */
            await Task.Delay(100);              /* Wait at least 100mS before sending commands  */
        }

        /* Initialize the GPIO */
        private void InitGpio()
        {
            IoController = GpioController.GetDefault(); /* Get the default GPIO controller on the system */
            if (IoController == null)
            {
                throw new Exception("GPIO does not exist on the current system.");
            }

            /* Initialize a pin as output for the Data/Command line on the display  */
            DataCommandPin = IoController.OpenPin(DATA_COMMAND_PIN);
            DataCommandPin.Write(GpioPinValue.High);
            DataCommandPin.SetDriveMode(GpioPinDriveMode.Output);

            /* Initialize a pin as output for the hardware Reset line on the display */
            ResetPin = IoController.OpenPin(RESET_PIN);
            ResetPin.Write(GpioPinValue.High);
            ResetPin.SetDriveMode(GpioPinDriveMode.Output);

        }

        /* Send graphics data to the screen */
        private void DisplaySendData(byte[] Data)
        {
            /* When the Data/Command pin is high, SPI data is treated as graphics data  */
            DataCommandPin.Write(GpioPinValue.High);
            SpiDisplay.Write(Data);
        }

        /* Send commands to the screen */
        private void DisplaySendCommand(byte[] Command)
        {
            /* When the Data/Command pin is low, SPI data is treated as commands for the display controller */
            DataCommandPin.Write(GpioPinValue.Low);
            SpiDisplay.Write(Command);
        }

        /* Writes the Display Buffer out to the physical screen for display */
        private void DisplayUpdate()
        {
            int Index = 0;
            /* We convert our 2-dimensional array into a serialized string of bytes that will be sent out to the display */
            for (int PageY = 0; PageY < SCREEN_HEIGHT_PAGES; PageY++)
            {
                for (int PixelX = 0; PixelX < SCREEN_WIDTH_PX; PixelX++)
                {
                    SerializedDisplayBuffer[Index] = DisplayBuffer[PixelX, PageY];
                    Index++;
                }
            }

            /* Write the data out to the screen */
            DisplaySendCommand(CMD_RESETCOLADDR);         /* Reset the column address pointer back to 0 */
            DisplaySendCommand(CMD_RESETPAGEADDR);        /* Reset the page address pointer back to 0   */
            DisplaySendData(SerializedDisplayBuffer);     /* Send the data over SPI                     */
        }

        /* 
         * NAME:        WriteLineDisplayBuf
         * DESCRIPTION: Writes a string to the display screen buffer (DisplayUpdate() needs to be called subsequently to output the buffer to the screen)
         * INPUTS:
         *
         * Line:      The string we want to render. In this sample, special characters like tabs and newlines are not supported.
         * Col:       The horizontal column we want to start drawing at. This is equivalent to the 'X' axis pixel position.
         * Row:       The vertical row we want to write to. The screen is divided up into 4 rows of 16 pixels each, so valid values for Row are 0,1,2,3.
         *
         * RETURN VALUE:
         * None. We simply return when we encounter characters that are out-of-bounds or aren't available in the font.
         */
        private void WriteLineDisplayBuf(String Line, UInt32 Col, UInt32 Row)
        {
            if (!DisplayUnavailable)
            {
                UInt32 CharWidth = 0;
                foreach (Char Character in Line)
                {
                    CharWidth = WriteCharDisplayBuf(Character, Col, Row);
                    Col += CharWidth;   /* Increment the column so we can track where to write the next character   */
                    if (CharWidth == 0) /* Quit if we encounter a character that couldn't be printed                */
                    {
                        return;
                    }
                }
            }
        }

        /* 
         * NAME:        WriteCharDisplayBuf
         * DESCRIPTION: Writes one character to the display screen buffer (DisplayUpdate() needs to be called subsequently to output the buffer to the screen)
         * INPUTS:
         *
         * Character: The character we want to draw. In this sample, special characters like tabs and newlines are not supported.
         * Col:       The horizontal column we want to start drawing at. This is equivalent to the 'X' axis pixel position.
         * Row:       The vertical row we want to write to. The screen is divided up into 4 rows of 16 pixels each, so valid values for Row are 0,1,2,3.
         *
         * RETURN VALUE:
         * We return the number of horizontal pixels used. This value is 0 if Row/Col are out-of-bounds, or if the character isn't available in the font.
         */
        private UInt32 WriteCharDisplayBuf(Char Chr, UInt32 Col, UInt32 Row)
        {
            /* Check that we were able to find the font corresponding to our character */
            FontCharacterDescriptor CharDescriptor = DisplayFontTable.GetCharacterDescriptor(Chr);
            if (CharDescriptor == null)
            {
                return 0;
            }

            /* Make sure we're drawing within the boundaries of the screen buffer */
            UInt32 MaxRowValue = (SCREEN_HEIGHT_PAGES / DisplayFontTable.FontHeightBytes) - 1;
            UInt32 MaxColValue = SCREEN_WIDTH_PX;
            if (Row > MaxRowValue)
            {
                return 0;
            }
            if ((Col + CharDescriptor.CharacterWidthPx + DisplayFontTable.FontCharSpacing) > MaxColValue)
            {
                return 0;
            }

            UInt32 CharDataIndex = 0;
            UInt32 StartPage = Row * 2;
            UInt32 EndPage = StartPage + CharDescriptor.CharacterHeightBytes;
            UInt32 StartCol = Col;
            UInt32 EndCol = StartCol + CharDescriptor.CharacterWidthPx;
            UInt32 CurrentPage = 0;
            UInt32 CurrentCol = 0;

            /* Copy the character image into the display buffer */
            for (CurrentPage = StartPage; CurrentPage < EndPage; CurrentPage++)
            {
                for (CurrentCol = StartCol; CurrentCol < EndCol; CurrentCol++)
                {
                    DisplayBuffer[CurrentCol, CurrentPage] = CharDescriptor.CharacterData[CharDataIndex];
                    CharDataIndex++;
                }
            }

            /* Pad blank spaces to the right of the character so there exists space between adjacent characters */
            for (CurrentPage = StartPage; CurrentPage < EndPage; CurrentPage++)
            {
                for (; CurrentCol < EndCol + DisplayFontTable.FontCharSpacing; CurrentCol++)
                {
                    DisplayBuffer[CurrentCol, CurrentPage] = 0x00;
                }
            }

            /* Return the number of horizontal pixels used by the character */
            return CurrentCol - StartCol;
        }

        /* Sets all pixels in the screen buffer to 0 */
        private void ClearDisplayBuf()
        {
            Array.Clear(DisplayBuffer, 0, DisplayBuffer.Length);
        }



        private void Unloaded(object sender, object args)
        {
            /* Cleanup */
            SpiDisplay.Dispose();
            ResetPin.Dispose();
            DataCommandPin.Dispose();
        }



    }

    public class FontCharacterDescriptor
    {
        public readonly char Character;
        public readonly UInt32 CharacterWidthPx;
        public readonly UInt32 CharacterHeightBytes;
        public readonly byte[] CharacterData;

        public FontCharacterDescriptor(Char Chr, UInt32 CharHeightBytes, byte[] CharData)
        {
            Character = Chr;
            CharacterWidthPx = (UInt32)CharData.Length / CharHeightBytes;
            CharacterHeightBytes = CharHeightBytes;
            CharacterData = CharData;
        }
    }

    /* This class contains the character data needed to output render text on the display */
    public static class DisplayFontTable
    {
        public static readonly UInt32 FontHeightBytes = 2;  /* Height of the characters. A value of 2 would indicate a 16 (2*8) pixel tall character */
        public static readonly UInt32 FontCharSpacing = 1;  /* Number of blank horizontal pixels to insert between adjacent characters               */

        /* Takes and returns the character descriptor for the corresponding Char if it exists */
        public static FontCharacterDescriptor GetCharacterDescriptor(Char Chr)
        {
            foreach (FontCharacterDescriptor CharDescriptor in FontTable)
            {
                if (CharDescriptor.Character == Chr)
                {
                    return CharDescriptor;
                }
            }
            return null;
        }

        /* Table with all the character data */
        private static readonly FontCharacterDescriptor[] FontTable =
        {
            new FontCharacterDescriptor(' ' ,FontHeightBytes,new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('!' ,FontHeightBytes,new byte[]{0xFE,0x05}),
            new FontCharacterDescriptor('"' ,FontHeightBytes,new byte[]{0x1E,0x00,0x1E,0x00,0x00,0x00}),
            new FontCharacterDescriptor('#' ,FontHeightBytes,new byte[]{0x80,0x90,0xF0,0x9E,0xF0,0x9E,0x10,0x00,0x07,0x00,0x07,0x00,0x00,0x00}),
            new FontCharacterDescriptor('$' ,FontHeightBytes,new byte[]{0x38,0x44,0xFE,0x44,0x98,0x02,0x04,0x0F,0x04,0x03}),
            new FontCharacterDescriptor('%' ,FontHeightBytes,new byte[]{0x0C,0x12,0x12,0x8C,0x40,0x20,0x10,0x88,0x84,0x00,0x00,0x02,0x01,0x00,0x00,0x00,0x03,0x04,0x04,0x03}),
            new FontCharacterDescriptor('&' ,FontHeightBytes,new byte[]{0x80,0x5C,0x22,0x62,0x9C,0x00,0x00,0x03,0x04,0x04,0x04,0x05,0x02,0x05}),
            new FontCharacterDescriptor('\'',FontHeightBytes,new byte[]{0x1E,0x00}),
            new FontCharacterDescriptor('(' ,FontHeightBytes,new byte[]{0xF0,0x0C,0x02,0x07,0x18,0x20}),
            new FontCharacterDescriptor(')' ,FontHeightBytes,new byte[]{0x02,0x0C,0xF0,0x20,0x18,0x07}),
            new FontCharacterDescriptor('*' ,FontHeightBytes,new byte[]{0x14,0x18,0x0E,0x18,0x14,0x00,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('+' ,FontHeightBytes,new byte[]{0x40,0x40,0xF0,0x40,0x40,0x00,0x00,0x01,0x00,0x00}),
            new FontCharacterDescriptor(',' ,FontHeightBytes,new byte[]{0x00,0x00,0x08,0x04}),
            new FontCharacterDescriptor('-' ,FontHeightBytes,new byte[]{0x40,0x40,0x40,0x40,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('.' ,FontHeightBytes,new byte[]{0x00,0x04}),
            new FontCharacterDescriptor('/' ,FontHeightBytes,new byte[]{0x00,0x80,0x70,0x0E,0x1C,0x03,0x00,0x00}),
            new FontCharacterDescriptor('0' ,FontHeightBytes,new byte[]{0xFC,0x02,0x02,0x02,0xFC,0x03,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('1' ,FontHeightBytes,new byte[]{0x04,0x04,0xFE,0x00,0x00,0x07}),
            new FontCharacterDescriptor('2' ,FontHeightBytes,new byte[]{0x0C,0x82,0x42,0x22,0x1C,0x07,0x04,0x04,0x04,0x04}),
            new FontCharacterDescriptor('3' ,FontHeightBytes,new byte[]{0x04,0x02,0x22,0x22,0xDC,0x02,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('4' ,FontHeightBytes,new byte[]{0xC0,0xA0,0x98,0x84,0xFE,0x00,0x00,0x00,0x00,0x07}),
            new FontCharacterDescriptor('5' ,FontHeightBytes,new byte[]{0x7E,0x22,0x22,0x22,0xC2,0x02,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('6' ,FontHeightBytes,new byte[]{0xFC,0x42,0x22,0x22,0xC4,0x03,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('7' ,FontHeightBytes,new byte[]{0x02,0x02,0xC2,0x32,0x0E,0x00,0x07,0x00,0x00,0x00}),
            new FontCharacterDescriptor('8' ,FontHeightBytes,new byte[]{0xDC,0x22,0x22,0x22,0xDC,0x03,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('9' ,FontHeightBytes,new byte[]{0x3C,0x42,0x42,0x22,0xFC,0x02,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor(':' ,FontHeightBytes,new byte[]{0x10,0x04}),
            new FontCharacterDescriptor(';' ,FontHeightBytes,new byte[]{0x00,0x10,0x08,0x04}),
            new FontCharacterDescriptor('<' ,FontHeightBytes,new byte[]{0x40,0xE0,0xB0,0x18,0x08,0x00,0x00,0x01,0x03,0x02}),
            new FontCharacterDescriptor('=' ,FontHeightBytes,new byte[]{0xA0,0xA0,0xA0,0xA0,0xA0,0x00,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('>' ,FontHeightBytes,new byte[]{0x08,0x18,0xB0,0xE0,0x40,0x02,0x03,0x01,0x00,0x00}),
            new FontCharacterDescriptor('?' ,FontHeightBytes,new byte[]{0x0C,0x02,0xC2,0x22,0x1C,0x00,0x00,0x05,0x00,0x00}),
            new FontCharacterDescriptor('@' ,FontHeightBytes,new byte[]{0xF0,0x0C,0x02,0x02,0xE1,0x11,0x11,0x91,0x72,0x02,0x0C,0xF0,0x00,0x03,0x04,0x04,0x08,0x09,0x09,0x08,0x09,0x05,0x05,0x00}),
            new FontCharacterDescriptor('A' ,FontHeightBytes,new byte[]{0x00,0x80,0xE0,0x98,0x86,0x98,0xE0,0x80,0x00,0x06,0x01,0x00,0x00,0x00,0x00,0x00,0x01,0x06}),
            new FontCharacterDescriptor('B' ,FontHeightBytes,new byte[]{0xFE,0x22,0x22,0x22,0x22,0x22,0xDC,0x07,0x04,0x04,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('C' ,FontHeightBytes,new byte[]{0xF8,0x04,0x02,0x02,0x02,0x02,0x04,0x08,0x01,0x02,0x04,0x04,0x04,0x04,0x02,0x01}),
            new FontCharacterDescriptor('D' ,FontHeightBytes,new byte[]{0xFE,0x02,0x02,0x02,0x02,0x02,0x04,0xF8,0x07,0x04,0x04,0x04,0x04,0x04,0x02,0x01}),
            new FontCharacterDescriptor('E' ,FontHeightBytes,new byte[]{0xFE,0x22,0x22,0x22,0x22,0x22,0x02,0x07,0x04,0x04,0x04,0x04,0x04,0x04}),
            new FontCharacterDescriptor('F' ,FontHeightBytes,new byte[]{0xFE,0x22,0x22,0x22,0x22,0x22,0x02,0x07,0x00,0x00,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('G' ,FontHeightBytes,new byte[]{0xF8,0x04,0x02,0x02,0x02,0x42,0x44,0xC8,0x01,0x02,0x04,0x04,0x04,0x04,0x02,0x07}),
            new FontCharacterDescriptor('H' ,FontHeightBytes,new byte[]{0xFE,0x20,0x20,0x20,0x20,0x20,0x20,0xFE,0x07,0x00,0x00,0x00,0x00,0x00,0x00,0x07}),
            new FontCharacterDescriptor('I' ,FontHeightBytes,new byte[]{0xFE,0x07}),
            new FontCharacterDescriptor('J' ,FontHeightBytes,new byte[]{0x00,0x00,0x00,0x00,0xFE,0x03,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('K' ,FontHeightBytes,new byte[]{0xFE,0x20,0x50,0x88,0x04,0x02,0x00,0x07,0x00,0x00,0x00,0x01,0x02,0x04}),
            new FontCharacterDescriptor('L' ,FontHeightBytes,new byte[]{0xFE,0x00,0x00,0x00,0x00,0x00,0x07,0x04,0x04,0x04,0x04,0x04}),
            new FontCharacterDescriptor('M' ,FontHeightBytes,new byte[]{0xFE,0x18,0x60,0x80,0x00,0x80,0x60,0x18,0xFE,0x07,0x00,0x00,0x01,0x06,0x01,0x00,0x00,0x07}),
            new FontCharacterDescriptor('N' ,FontHeightBytes,new byte[]{0xFE,0x04,0x18,0x20,0x40,0x80,0x00,0xFE,0x07,0x00,0x00,0x00,0x00,0x01,0x02,0x07}),
            new FontCharacterDescriptor('O' ,FontHeightBytes,new byte[]{0xF8,0x04,0x02,0x02,0x02,0x02,0x04,0xF8,0x01,0x02,0x04,0x04,0x04,0x04,0x02,0x01}),
            new FontCharacterDescriptor('P' ,FontHeightBytes,new byte[]{0xFE,0x42,0x42,0x42,0x42,0x42,0x24,0x18,0x07,0x00,0x00,0x00,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('Q' ,FontHeightBytes,new byte[]{0xF8,0x04,0x02,0x02,0x02,0x02,0x04,0xF8,0x01,0x02,0x04,0x04,0x04,0x05,0x02,0x05}),
            new FontCharacterDescriptor('R' ,FontHeightBytes,new byte[]{0xFE,0x42,0x42,0x42,0x42,0x42,0x64,0x98,0x00,0x07,0x00,0x00,0x00,0x00,0x00,0x00,0x03,0x04}),
            new FontCharacterDescriptor('S' ,FontHeightBytes,new byte[]{0x1C,0x22,0x22,0x22,0x42,0x42,0x8C,0x03,0x04,0x04,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('T' ,FontHeightBytes,new byte[]{0x02,0x02,0x02,0x02,0xFE,0x02,0x02,0x02,0x02,0x00,0x00,0x00,0x00,0x07,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('U' ,FontHeightBytes,new byte[]{0xFE,0x00,0x00,0x00,0x00,0x00,0x00,0xFE,0x01,0x02,0x04,0x04,0x04,0x04,0x02,0x01}),
            new FontCharacterDescriptor('V' ,FontHeightBytes,new byte[]{0x06,0x18,0x60,0x80,0x00,0x80,0x60,0x18,0x06,0x00,0x00,0x00,0x01,0x06,0x01,0x00,0x00,0x00}),
            new FontCharacterDescriptor('W' ,FontHeightBytes,new byte[]{0x0E,0x30,0xC0,0x00,0xC0,0x30,0x0E,0x30,0xC0,0x00,0xC0,0x30,0x0E,0x00,0x00,0x01,0x06,0x01,0x00,0x00,0x00,0x01,0x06,0x01,0x00,0x00}),
            new FontCharacterDescriptor('X' ,FontHeightBytes,new byte[]{0x06,0x08,0x90,0x60,0x60,0x90,0x08,0x06,0x06,0x01,0x00,0x00,0x00,0x00,0x01,0x06}),
            new FontCharacterDescriptor('Y' ,FontHeightBytes,new byte[]{0x06,0x08,0x10,0x20,0xC0,0x20,0x10,0x08,0x06,0x00,0x00,0x00,0x00,0x07,0x00,0x00,0x00,0x00}),
            new FontCharacterDescriptor('Z' ,FontHeightBytes,new byte[]{0x02,0x82,0x42,0x22,0x1A,0x06,0x06,0x05,0x04,0x04,0x04,0x04}),
            new FontCharacterDescriptor('[' ,FontHeightBytes,new byte[]{0xFE,0x02,0x02,0x3F,0x20,0x20}),
            new FontCharacterDescriptor('\\',FontHeightBytes,new byte[]{0x0E,0x70,0x80,0x00,0x00,0x00,0x03,0x1C}),
            new FontCharacterDescriptor('^' ,FontHeightBytes,new byte[]{0x02,0x02,0xFE,0x20,0x20,0x3F}),
            new FontCharacterDescriptor('_' ,FontHeightBytes,new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x10,0x10,0x10,0x10,0x10,0x10,0x10}),
            new FontCharacterDescriptor('`' ,FontHeightBytes,new byte[]{0x02,0x04,0x00,0x00}),
            new FontCharacterDescriptor('a' ,FontHeightBytes,new byte[]{0xA0,0x50,0x50,0x50,0x50,0xE0,0x00,0x03,0x04,0x04,0x04,0x04,0x03,0x04}),
            new FontCharacterDescriptor('b' ,FontHeightBytes,new byte[]{0xFE,0x20,0x10,0x10,0x10,0xE0,0x07,0x02,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('c' ,FontHeightBytes,new byte[]{0xE0,0x10,0x10,0x10,0x10,0x20,0x03,0x04,0x04,0x04,0x04,0x02}),
            new FontCharacterDescriptor('d' ,FontHeightBytes,new byte[]{0xE0,0x10,0x10,0x10,0x20,0xFE,0x03,0x04,0x04,0x04,0x02,0x07}),
            new FontCharacterDescriptor('e' ,FontHeightBytes,new byte[]{0xE0,0x90,0x90,0x90,0x90,0xE0,0x03,0x04,0x04,0x04,0x04,0x02}),
            new FontCharacterDescriptor('f' ,FontHeightBytes,new byte[]{0x10,0xFC,0x12,0x00,0x07,0x00}),
            new FontCharacterDescriptor('g' ,FontHeightBytes,new byte[]{0xE0,0x10,0x10,0x10,0x20,0xF0,0x03,0x24,0x24,0x24,0x22,0x1F}),
            new FontCharacterDescriptor('h' ,FontHeightBytes,new byte[]{0xFE,0x20,0x10,0x10,0xE0,0x07,0x00,0x00,0x00,0x07}),
            new FontCharacterDescriptor('i' ,FontHeightBytes,new byte[]{0xF2,0x07}),
            new FontCharacterDescriptor('j' ,FontHeightBytes,new byte[]{0x00,0xF2,0x20,0x1F}),
            new FontCharacterDescriptor('k' ,FontHeightBytes,new byte[]{0xFE,0x80,0xC0,0x20,0x10,0x00,0x07,0x00,0x00,0x01,0x02,0x04}),
            new FontCharacterDescriptor('l' ,FontHeightBytes,new byte[]{0xFE,0x07}),
            new FontCharacterDescriptor('m' ,FontHeightBytes,new byte[]{0xF0,0x20,0x10,0x10,0xE0,0x20,0x10,0x10,0xE0,0x07,0x00,0x00,0x00,0x07,0x00,0x00,0x00,0x07}),
            new FontCharacterDescriptor('n' ,FontHeightBytes,new byte[]{0xF0,0x20,0x10,0x10,0xE0,0x07,0x00,0x00,0x00,0x07}),
            new FontCharacterDescriptor('o' ,FontHeightBytes,new byte[]{0xE0,0x10,0x10,0x10,0x10,0xE0,0x03,0x04,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('p' ,FontHeightBytes,new byte[]{0xF0,0x20,0x10,0x10,0x10,0xE0,0x3F,0x02,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('q' ,FontHeightBytes,new byte[]{0xE0,0x10,0x10,0x10,0x20,0xF0,0x03,0x04,0x04,0x04,0x02,0x3F}),
            new FontCharacterDescriptor('r' ,FontHeightBytes,new byte[]{0xF0,0x20,0x10,0x07,0x00,0x00}),
            new FontCharacterDescriptor('s' ,FontHeightBytes,new byte[]{0x60,0x90,0x90,0x90,0x20,0x02,0x04,0x04,0x04,0x03}),
            new FontCharacterDescriptor('t' ,FontHeightBytes,new byte[]{0x10,0xFC,0x10,0x00,0x03,0x04}),
            new FontCharacterDescriptor('u' ,FontHeightBytes,new byte[]{0xF0,0x00,0x00,0x00,0xF0,0x03,0x04,0x04,0x02,0x07}),
            new FontCharacterDescriptor('v' ,FontHeightBytes,new byte[]{0x30,0xC0,0x00,0x00,0x00,0xC0,0x30,0x00,0x00,0x03,0x04,0x03,0x00,0x00}),
            new FontCharacterDescriptor('w' ,FontHeightBytes,new byte[]{0x30,0xC0,0x00,0xC0,0x30,0xC0,0x00,0xC0,0x30,0x00,0x01,0x06,0x01,0x00,0x01,0x06,0x01,0x00}),
            new FontCharacterDescriptor('x' ,FontHeightBytes,new byte[]{0x10,0x20,0xC0,0xC0,0x20,0x10,0x04,0x02,0x01,0x01,0x02,0x04}),
            new FontCharacterDescriptor('y' ,FontHeightBytes,new byte[]{0x30,0xC0,0x00,0x00,0x00,0xC0,0x30,0x20,0x20,0x13,0x0C,0x03,0x00,0x00}),
            new FontCharacterDescriptor('z' ,FontHeightBytes,new byte[]{0x10,0x90,0x50,0x30,0x06,0x05,0x04,0x04}),
            new FontCharacterDescriptor('{' ,FontHeightBytes,new byte[]{0x80,0x80,0x7C,0x02,0x02,0x00,0x00,0x1F,0x20,0x20}),
            new FontCharacterDescriptor('|' ,FontHeightBytes,new byte[]{0xFE,0x3F}),
            new FontCharacterDescriptor('}' ,FontHeightBytes,new byte[]{0x02,0x02,0x7C,0x80,0x80,0x20,0x20,0x1F,0x00,0x00}),
            new FontCharacterDescriptor('~' ,FontHeightBytes,new byte[]{0x0C,0x02,0x02,0x04,0x08,0x08,0x06,0x00,0x00,0x00,0x00,0x00,0x00,0x00}),
        };
    }
}