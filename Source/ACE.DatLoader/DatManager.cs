using System.IO;

using log4net;

namespace ACE.DatLoader
{
    public static class DatManager
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static string datFile;

        private static int count;

        // End of retail Iteration versions.
        public static string CLIENT_VERSION_STRING = "1802";
        private static int ITERATION_CELL = 982;
        private static int ITERATION_PORTAL = 2072;
        private static int ITERATION_HIRES = 497;
        private static int ITERATION_LANGUAGE = 994;

        public static string INFILTRATION_CLIENT_VERSION_STRING = "i102";
        private static int INFILTRATION_ITERATION_CELL = 10001;
        private static int INFILTRATION_ITERATION_PORTAL = 10005;
        private static int INFILTRATION_ITERATION_HIRES = 497;
        private static int INFILTRATION_ITERATION_LANGUAGE = 10002;

        public static string CUSTOMDM_CLIENT_VERSION_STRING = "c100";
        private static int CUSTOMDM_ITERATION_CELL = 20001;
        private static int CUSTOMDM_ITERATION_PORTAL = 20001;
        private static int CUSTOMDM_ITERATION_HIRES = 497;
        private static int CUSTOMDM_ITERATION_LANGUAGE = 994;
        public static CellDatDatabase CellDat { get; private set; }

        public static PortalDatDatabase PortalDat { get; private set; }
        public static DatDatabase HighResDat { get; private set; }
        public static LanguageDatDatabase LanguageDat { get; private set; }

        public static void Initialize(string datFileDirectory, bool keepOpen = false, bool loadCell = true)
        {
            string cellFilename;
            string portalFilename;
            string highResFilename;
            string languageFilename;

            int cellExpectedIteration;
            int portalExpectedIteration;
            int highResExpectedIteration;
            int languageExpectedIteration;

            switch (Common.ConfigManager.Config.Server.WorldRuleset)
            {
                default:
                case Common.Ruleset.EoR:
                    cellFilename = "client_cell_1.dat";
                    portalFilename = "client_portal.dat";
                    highResFilename = "client_highres.dat";
                    languageFilename = "client_local_English.dat";

                    cellExpectedIteration = ITERATION_CELL;
                    portalExpectedIteration = ITERATION_PORTAL;
                    highResExpectedIteration = ITERATION_HIRES;
                    languageExpectedIteration = ITERATION_LANGUAGE;
                    break;
                case Common.Ruleset.Infiltration:
                    cellFilename = "infilt_cell_1.dat";
                    portalFilename = "infilt_portal.dat";
                    highResFilename = "infilt_highres.dat";
                    languageFilename = "infilt_local_English.dat";

                    cellExpectedIteration = INFILTRATION_ITERATION_CELL;
                    portalExpectedIteration = INFILTRATION_ITERATION_PORTAL;
                    highResExpectedIteration = INFILTRATION_ITERATION_HIRES;
                    languageExpectedIteration = INFILTRATION_ITERATION_LANGUAGE;
                    break;
                case Common.Ruleset.CustomDM:
                    cellFilename = "custDM_cell_1.dat";
                    portalFilename = "custDM_portal.dat";
                    highResFilename = "custDM_highres.dat";
                    languageFilename = "custDM_local_English.dat";

                    cellExpectedIteration = CUSTOMDM_ITERATION_CELL;
                    portalExpectedIteration = CUSTOMDM_ITERATION_PORTAL;
                    highResExpectedIteration = CUSTOMDM_ITERATION_HIRES;
                    languageExpectedIteration = CUSTOMDM_ITERATION_LANGUAGE;
                    break;
            }

            var datDir = Path.GetFullPath(Path.Combine(datFileDirectory));

            if (loadCell)
            {
                try
                {
                    datFile = Path.Combine(datDir, cellFilename);
                    CellDat = new CellDatDatabase(datFile, keepOpen);
                    count = CellDat.AllFiles.Count;
                    log.Info($"Successfully opened {datFile} file, containing {count} records, iteration {CellDat.Iteration}");
                    if (CellDat.Iteration != cellExpectedIteration)
                        log.Warn($"{datFile} iteration {CellDat.Iteration} does not match expected version of {cellExpectedIteration}.");
                }
                catch (FileNotFoundException ex)
                {
                    log.Error($"An exception occured while attempting to open {datFile} file!  This needs to be corrected in order for Landblocks to load!");
                    log.Error($"Exception: {ex.Message}");
                }
            }

            try
            {
                datFile = Path.Combine(datDir, portalFilename);
                PortalDat = new PortalDatDatabase(datFile, keepOpen);
                count = PortalDat.AllFiles.Count;
                log.Info($"Successfully opened {datFile} file, containing {count} records, iteration {PortalDat.Iteration}");
                if (PortalDat.Iteration != portalExpectedIteration)
                    log.Warn($"{datFile} iteration {PortalDat.Iteration} does not match expected version of {portalExpectedIteration}.");
            }
            catch (FileNotFoundException ex)
            {
                log.Error($"An exception occured while attempting to open {datFile} file!\n\n *** Please check your 'DatFilesDirectory' setting in the config.js file. ***\n *** ACE will not run properly without this properly configured! ***\n");
                log.Error($"Exception: {ex.Message}");
            }

            // Load the client_highres.dat file. This is not required for ACE operation, so no exception needs to be generated.
            datFile = Path.Combine(datDir, highResFilename);
            if (File.Exists(datFile))
            {
                HighResDat = new DatDatabase(datFile, keepOpen);
                count = HighResDat.AllFiles.Count;
                log.Info($"Successfully opened {datFile} file, containing {count} records, iteration {HighResDat.Iteration}");
                if (HighResDat.Iteration != highResExpectedIteration)
                    log.Warn($"{datFile} iteration {HighResDat.Iteration} does not match expected iteration version of {highResExpectedIteration}.");
            }

            try
            {
                datFile = Path.Combine(datDir, languageFilename);
                LanguageDat = new LanguageDatDatabase(datFile, keepOpen);
                count = LanguageDat.AllFiles.Count;
                log.Info($"Successfully opened {datFile} file, containing {count} records, iteration {LanguageDat.Iteration}");
                if (LanguageDat.Iteration != languageExpectedIteration)
                    log.Warn($"{datFile} iteration {LanguageDat.Iteration} does not match expected version of {languageExpectedIteration}.");
            }
            catch (FileNotFoundException ex)
            {
                log.Error($"An exception occured while attempting to open {datFile} file!\n\n *** Please check your 'DatFilesDirectory' setting in the config.json file. ***\n *** ACE will not run properly without this properly configured! ***\n");
                log.Error($"Exception: {ex.Message}");
            }
        }
    }
}
