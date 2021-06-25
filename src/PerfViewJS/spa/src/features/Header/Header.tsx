import { CommandBar, getTheme, ICommandBarItemProps, ITheme, Text } from "@fluentui/react";
import { useDataFileContext } from "context/DataFileContext";
import { useLocalStorage } from "hooks/useLocalStorage";
import React from "react";
import { Container, Row, Col } from "react-grid-system";
import {
  AzureThemeHighContrastDark,
  AzureThemeHighContrastLight,
  AzureThemeLight,
  AzureThemeDark,
} from "@fluentui/azure-themes";

type ThemeTypes = "Light" | "Dark" | "Dark High Contrast" | "Light High Contrast";

export type IThemeMap = {
  [key in ThemeTypes]: ITheme;
};

export const AvailableThemes: IThemeMap = {
  Light: AzureThemeLight,
  Dark: AzureThemeDark,
  "Dark High Contrast": AzureThemeHighContrastDark,
  "Light High Contrast": AzureThemeHighContrastLight,
};

const theme = getTheme();

const dataFileRowStyle = { borderBottom: `1px solid ${theme.semanticColors.bodyDivider}` };
const Header: React.FC = () => {
  const { dataFileName } = useDataFileContext();
  const [, setTheme] = useLocalStorage<keyof IThemeMap>("theme", "Light");

  const themes: ICommandBarItemProps[] = [
    {
      key: "newItem",
      text: "Theme",
      iconProps: { iconName: "Design" },
      subMenuProps: {
        items: [
          {
            key: "dark",
            text: "Dark",
            iconProps: { iconName: "CircleFill" },
            onClick: () => setTheme("Dark"),
          },
          {
            key: "light",
            text: "Light",
            iconProps: { iconName: "CircleRing" },
            onClick: () => setTheme("Light"),
          },
          {
            key: "darkContrast",
            text: "Dark high contrast",
            iconProps: { iconName: "CircleStopSolid" },
            onClick: () => setTheme("Dark High Contrast"),
          },
          {
            key: "lightContrast",
            text: "Light high constrast",
            iconProps: { iconName: "CircleStop" },
            onClick: () => setTheme("Light High Contrast"),
          },
        ],
      },
    },
  ];

  return (
    <Container fluid>
      <Row align="center" style={dataFileRowStyle}>
        <Col xs={6}>
          <Text>{dataFileName}</Text>
        </Col>
        <Col xs={6}>
          <CommandBar styles={{ root: { border: "none" } }} items={[]} farItems={themes} />
        </Col>
      </Row>
    </Container>
  );
};

export { Header };
