-- MySQL dump 10.13  Distrib 8.0.22, for Linux (x86_64)
--
-- Host: localhost    Database: ace_shard_session_log
-- ------------------------------------------------------
-- Server version	8.0.22

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Current Database: `ace_shard_session_log`
--

/*!40000 DROP DATABASE IF EXISTS `ace_shard_session_log`*/;

CREATE DATABASE /*!32312 IF NOT EXISTS*/ `ace_shard_session_log` /*!40100 DEFAULT CHARACTER SET utf8mb4 */ /*!80016 DEFAULT ENCRYPTION='N' */;

USE `ace_shard_session_log`;

--
-- Table structure for table `account_session_log`
--

DROP TABLE IF EXISTS `account_session_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `account_session_log` (
  `sessionLogId` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `accountId` INT UNSIGNED NOT NULL,
  `accountName` VARCHAR(50) NOT NULL,  
  `sessionIP` VARCHAR(45),
  `loginDateTime` DATETIME,  
  PRIMARY KEY (`sessionLogId`)  
) ENGINE=INNODB DEFAULT CHARSET=utf8mb4;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Table structure for table `character_login_log`
--

DROP TABLE IF EXISTS `character_login_log`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `character_login_log` (
  `characterLoginLogId` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `accountId` INT UNSIGNED NOT NULL,
  `accountName` VARCHAR(50) NOT NULL, 
  `characterId` INT UNSIGNED NOT NULL,
  `characterName` VARCHAR(50) NOT NULL,   
  `sessionIP` VARCHAR(45),
  `loginDateTime` DATETIME,  
  PRIMARY KEY (`characterLoginLogId`)  
) ENGINE=INNODB DEFAULT CHARSET=utf8mb4;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

