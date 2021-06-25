import { FontWeights, getTheme, IButtonStyles, IIconProps, mergeStyleSets } from "@fluentui/react";

const theme = getTheme();

export const wrapperStyle: React.CSSProperties = { height: "100vh", position: "relative" };

export const cancelIcon: IIconProps = { iconName: "Cancel" };

export const iconButtonStyles: Partial<IButtonStyles> = {
  root: {
    marginLeft: "auto",
    marginTop: "4px",
    marginRight: "2px",
  },
};

export const contentStyles = mergeStyleSets({
  container: {
    display: "flex",
    flexFlow: "column nowrap",
    alignItems: "stretch",
  },
  header: [
    {
      flex: "1 1 auto",
      borderTop: `4px solid ${theme.palette.themePrimary}`,
      display: "flex",
      alignItems: "center",
      fontWeight: FontWeights.semibold,
      padding: "3px 12px 3px 24px",
    },
  ],
  body: {
    flex: "4 4 auto",
    padding: "0 24px 0 24px",
    overflowY: "hidden",
  },
});
